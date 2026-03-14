Analiza zakomentowanego kodu — wnioski i plan
1. Ewolucja architektury sieciowej (czy autor miał rację?)
Tak, absolutnie. Kod pokazuje trzy pokolenia podejścia:

Generacja 1 — synchroniczne UDP (SendPacket):
Metoda SendPacket (ClientForm.cs:50) wysyłała pakiet UDP i czekała na odpowiedź w tej samej linii wywołania. To podejście request-response jest fundamentalnie złe dla gry: każda klatka blokowała UI thread, brak było obsługi opóźnień, a odrzucone pakiety UDP powodowały zawieszenie.

Generacja 2 — asynchroniczny listener z playerCount:
Autor dodał wątek nasłuchu i playerCount do wykrywania dołączenia/odejścia graczy (blok z liniami 354–408). Problem: używano UDP (zawodnego) do zarządzania listą graczy. Jedno zagubione odejście i lista jest nieodwracalnie desynchronizowana przez cały wyścig.

Generacja 3 (bieżąca) — TCP/UDP dual-channel z eventami:
TCP tylko dla zdarzeń (Join, Disconnect, PowerUp, Race), UDP tylko dla koordynatów. To jest właściwy model i autor słusznie migrował do niego.

2. Koncepcja power-upów — trzy podejścia i ich problemy
Podejście 1 — polling w UDP loop (UpdateClient, linie 671–699 zakomentowane):
Autor śledził powerUpsUsed[i] per przeciwnik — licznik rośnie przy każdym użyciu. Gdy licznik wzrośnie, wysyłaj pakiet TCP. Problem logiczny: int[] powerUpsUsed jest nadal zaalokowane w konstruktorze formularza (linia 91) i zainicjalizowane zerami, ale w aktywnej ścieżce kodu nigdy nie jest aktualizowane. Tablica to artefakt tej porzuconej koncepcji.

Podejście 2 — flaga sentLastUsedPowerUp w pętli UDP (linie 537–560 zakomentowane):
Bardziej eleganckie: śledź lokalnie ostatni znany PowerUpType, gdy gra go "skonsumuje" (PowerUpType → 0), wyślij go raz przez TCP. Tu jest jednak subtelny błąd logiczny — zmienna sentLastUsedPowerUp nigdy nie była ustawiana na true we właściwym miejscu. Komentarze sugerują, że autor to zauważył i dlatego tę całą sekcję porzucił.

Podejście 3 (bieżące) — event Player_PowerUpUsed:
Driver.cs pollinguje Brick i WhiteBricks w osobnym wątku i odpala event gdy Brick zmieni się na Brick.None (czyli power-up właśnie skonsumowany). To jest prawidłowe i czyste — ale brak jest drugiej połowy: obsługa przychodzącego PacketType.PowerUp (linia 316) tylko pokazuje MessageBox.Show(data.Packet.Content). Wywołanie gameClient.Opponents[x].UsePowerUp() nigdy nie zostało podpięte.

Wniosek: UsePowerUp(Brick, int whiteBricks) w Driver.cs jest w pełni zaimplementowana — zawiera shellcode x86 z hardcoded konstantami edx per slot (0xD1, 0xA4, 0xBA, 0x49, 0x8D dla przeciwników 1-5, wszystko odkryte przez reverse engineering). API czeka na podpięcie.

3. VectorZ — celowe pominięcie
W OLD i NEW ścieżce kodu identycznie zakomentowane:


//opponents[i].VectorZ1 = enemy.VectorZ1;//opponents[i].VectorZ2 = enemy.VectorZ2;
To nie jest pominięcie z lenistwa. Gra przechowuje orientację pojazdu jako dwa wektory 3D (forward i up). Pojazd porusza się po powierzchni toru, więc:

VectorZ1 (składowa Z wektora "do przodu") = jest wyznaczona przez pochylenie terenu, nie przez kierowcę
VectorZ2 (składowa Z wektora "w górę") = prawie zawsze bliski 1.0 na płaskim terenie
Autor odkrył eksperymentalnie, że 4 komponenty wystarczają do poprawnego wyświetlenia rotacji przeciwnika. Pisanie Z-składowych do slotów przeciwnika może powodować artefakty (pojazd przechyla się pod dziwnym kątem gdy oba wektory nie są ortogonalne). To jest świadoma decyzja, należy ją zachować.

4. RemoveMenuButtons() — niezapięty mechanizm
Metoda RemoveMenuButtons() w GameClient.cs jest w pełni zaimplementowana:

Ukrywa przyciski Build, Circuit, Single Race, Time Attack, Options z menu głównego
Zmienia przycisk Versus na "WAITING FOR SERVER TO START A RACE..."
Używa shellcode + memory patching, wszystkie offsety odkryte (0x1058, 0x498, 0x1348 etc.)
Nigdzie w ClientForm.cs nie ma wywołania tej metody. Należy ją wywołać przy InitializedType.Core po połączeniu z serwerem. Jej brak powoduje, że lokalny gracz może sam wchodzić do menu i startować wyścig bez synchronizacji z serwerem — co kończy się rozjechaniem stanów.

5. Zombie cleanup — zakomentowany timer
ServerForm.cs:250-255:


//gameServer.Participants.RemoveAll(p => p.LastActivity < DateTime.Now.AddSeconds(-5));
ServerParticipant ma LastActivity który jest ustawiany przy każdym pakiecie UDP koordynatów. Mechanizm jest gotowy, timer jest skonfigurowany, ale kod zakomentowany. Bez tego: crash gry u gracza, crash klienta, utrata połączenia — serwer nie wie o rozłączeniu do końca sesji. Wszyscy pozostali gracze widzą "zamrożonego" przeciwnika na jednym miejscu.

Dlaczego zakomentowany? Prawdopodobnie dlatego, że cleanup działa z wątku UI timera, a lista participants jest dostępna bez blokady. To ten sam race condition co P0.1 — naprawiony już przez _participantsLock.

6. AIDriversAmount — brakujące uzbrojenie wyścigu
GameClient.cs ma właściwość AIDriversAmount która pisze liczbę AI do pamięci gry. Przy recepcji PacketType.Race w ClientForm.cs:323:


gameClient.SetupRace(Int32.Parse(packetPart[0]), Int32.Parse(packetPart[1]));
Brak jest gameClient.AIDriversAmount = participants.Count - 1 (ustaw liczbę AI na liczbę graczy sieciowych minus lokalny). Bez tego gra startuje z domyślną liczbą AI botów. Sloty Opponents[0..4] są jednocześnie zajęte przez AI i przez sieciowych graczy — pisanie koordynatów sieciowych do pamięci kontra AI engine który też pisze do tych samych adresów.

7. currentDriverNumber — ukryty bug reset
Driver.cs:39: private static int currentDriverNumber — statyczny licznik. Każdy new Driver / new Player / new Opponent go inkrementuje. Player=0, Opponents[0..4]=1..5.

UsePowerUp() w Driver.cs:390 switchuje po driverNumber (0=player, 1–5=opponents). Po Unload() i reinicjalizacji (nowy wyścig) licznik nie jest resetowany. Drugi wyścig: Player dostaje numer 6, Opponents 7–11 — UsePowerUp() wchodzi do default: return i milcząco nic nie robi. Power-upy przestają działać po pierwszym wyścigu.

Plan wdrożenia (kolejność i zależności)
Etap 1 — Uzupełnienie flow multiplayerowego (bez nowych funkcjonalności)

Punkt wejścia: client_PacketReceived case PacketType.Race (ClientForm.cs:321).

Po odebraniu Race packet:

Oblicz liczbę sieciowych przeciwników = participants.Count - 1
Wywołaj gameClient.AIDriversAmount = <liczba> przed SetupRace, żeby gra zarezerwowała właściwe sloty
Wywołaj gameClient.LoadRRB = false żeby wyłączyć ścieżki AI (już jest w gameClient_Initialized ale powinno też być tutaj)
SetupRace już jest podpięte
Efekt: gracze sieciowi zastępują AI, nie nakładają się na nie.

Etap 2 — Podpięcie power-upów sieciowych

Problem: pakiet PacketType.PowerUp niesie type|whiteBricks ale nie niesie informacji KTO go użył. Klient nie wie który Opponents[i].UsePowerUp() wywołać.

Protokół do zmiany serwerowo i kliencko:

Nadawca wysyła: nickname|type|whiteBricks
Serwer broadcastuje to samo
Odbiorca: szuka nickname w participants, mapuje na index → gameClient.Opponents[index].UsePowerUp(brick, whiteBricks)
Punkt wejścia: Player_PowerUpUsed (wysyłanie) i case PacketType.PowerUp (odbiór).

Etap 3 — Reset currentDriverNumber

Przy każdym Unload() (lub przy inicjalizacji nowego wyścigu) wyzerować currentDriverNumber. Wymaga albo metody statycznej Driver.ResetDriverCounter(), albo zmiany architektury na instancyjną (jeden licznik per GameClient). Drugie podejście jest bardziej odporne przy hipotetycznym wielokrotnym starcie wyścigu.

Etap 4 — Podpięcie RemoveMenuButtons()

W gameClient_Initialized case InitializedType.Core, po gameClient.RunInBackground = true:

Sprawdź czy klient jest połączony z serwerem
Jeśli tak: gameClient.RemoveMenuButtons()
Jeśli nie: poczekaj na połączenie i wywołaj wtedy
Alternatywnie: wywołaj przy recepcji pierwszego jakiegokolwiek TCP pakietu.

Etap 5 — Zombie cleanup

Odkomentować logikę w timerCleanUp_Tick, ale wykonać cleanup pod _participantsLock. Przy usunięciu gracza przez timeout: DisconnectParticipant(p) który broadcastuje Disconnect do pozostałych.

Wartość 5 sekund to za mało — UDP co 10ms, ale przy chwilowym spike niezawodności UDP można dostać fałszywe wybudzenia. Rekomendacja: 15–30 sekund. Trzeba też pamiętać że LastActivity nie jest aktualizowane gdy gracz jest w lobby (przed startem wyścigu) — wtedy UDP koordynaty nie płyną. Albo osobny TCP heartbeat, albo granulacja: cleanup tylko gdy raceActive.

Podsumowanie oceny autora
Decyzja autora	Ocena
Migracja UDP request-response → TCP/UDP dual channel	Prawidłowa, standardowy model gier
Polling power-upów → eventy	Prawidłowa, event-driven jest czysty
Odrzucenie playerCount diffing → TCP Join/Disconnect	Prawidłowa, UDP to zły nośnik dla stanu listy
Pominięcie VectorZ1/Z2	Prawdopodobnie prawidłowa, wymaga weryfikacji na pochyłościach
RemoveMenuButtons() — niezapięte	Porzucone zbyt wcześnie, gotowe API czeka na użycie
Power-up receive — tylko MessageBox	Niekompletne, UsePowerUp() gotowe i czeka
Zombie cleanup — zakomentowane	Słuszna idea, zatrzymana przez wcześniejszy race condition (już naprawiony)
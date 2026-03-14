Code Review: LEGO Racers Online
Projekt: Mod dodający multiplayer online do gry LEGO Racers (1999)
Technologie: C# / .NET 4.0 / WinForms / TCP+UDP / P/Invoke
Oryginalny autor: Roel van de Water (2015)

1. Problemy krytyczne (bugi / awarie)
1.1 Brak thread safety -- race conditions wszędzie
Server.cs:122 -- lista participants jest modyfikowana i iterowana z dwóch wątków (TCP i UDP) bez żadnej synchronizacji:

Wynik: InvalidOperationException ("Collection was modified during enumeration") lub uszkodzenie danych. Na serwerze z wieloma graczami to gwarantowany crash.

1.2 Busy-wait loop w serwerze TCP bez Thread.Sleep
Server.cs:113 -- while(true) w TcpListener() nie zawiera żadnego Thread.Sleep. Gdy started == false lub nie ma danych, pętla konsumuje 100% CPU jednego rdzenia.

1.3 Współdzielony bufor bytes w kliencie
Client.cs:60 -- pole bytes jest współdzielone między wątkami TCP i UDP:

Wątek UDP nadpisuje bufor, który wątek TCP akurat czyta -- powoduje uszkodzenie danych.

1.4 Serwer używa udpServer.Send z referencją ipEndPoint zamiast adresu nadawcy
Server.cs:266 -- serwer odsyła dane UDP do ipEndPoint, co jest współdzielonym polem klasy nadpisywanym przez Receive(). Oznacza to, że odpowiedź może trafić do złego gracza, jeśli w międzyczasie przyszedł pakiet od kogoś innego.

1.5 Thread.Abort() -- przestarzałe i niebezpieczne
Server.cs:93 -- Thread.Abort() może pozostawić obiekty w niespójnym stanie. W .NET Core/5+ ta metoda w ogóle nie istnieje.

1.6 throw exc zamiast throw w kliencie
Client.cs:137 -- throw exc; kasuje oryginalny stack trace. Powinno być throw;.

2. Problemy architektoniczne
2.1 Tekstowy protokół sieciowy z float.Parse()
Server.cs:243-253 -- koordynaty są serializowane jako tekst z | i ; jako separatory, a potem parsowane przez float.Parse(). Problemy:

Zależność od locale: float.Parse("1.5") zwróci błąd na systemach z polskim locale, gdzie separator dziesiętny to , zamiast .
Wydajność: tekst jest ~4x większy niż binarne float (4 bajty vs "123.456789" = 10 bajtów)
Dla UDP przy 30-60 Hz to marnowanie przepustowości i cykli CPU
2.2 Brak delimitacji pakietów TCP
Server.cs:130, Client.cs:81 -- TCP to strumień bajtów, nie wiadomości. Kod czyta stream.Read(bytes, 0, 64) i zakłada, że otrzyma dokładnie jeden kompletny pakiet. W rzeczywistości:

Dwa pakiety mogą przyjść w jednym Read() (łączenie)
Jeden pakiet może być podzielony na dwa Read() (fragmentacja)
Wynik: losowe uszkodzenie danych i crash w Packet.Populate().
2.3 Konstruktor Player/Opponent z 20 parametrami
Player.cs:29 -- konstruktor przyjmuje 20 parametrów. To jest ekstremalny code smell. Powinien przyjmować obiekt konfiguracyjny lub struct.

2.4 Hardkodowane magiczne wartości wszędzie
GameClient.cs:397-534 -- cała nawigacja menu opiera się na hardkodowanych offsetach hex (0x498, 0x1058, itd.) bez stałych z nazwami. Jeden błąd i debug trwa godziny.

2.5 Logika multiplayer zapisuje tylko do Opponents[0]
ClientForm.cs:479-496 -- mimo pętli foreach, wszystkie dane zdalnych graczy trafiają do gameClient.Opponents[0] z break na końcu. Tylko 1 zdalny gracz jest widoczny w grze. To wygląda jak niedokończona implementacja -- indeks powinien zależeć od numeru gracza.

3. Problemy jakości kodu
3.1 Ogromna ilość zakomentowanego kodu
ClientForm.cs -- ponad 50% pliku to zakomentowany kod. Linie 65-87, 369-468, 525-698, 702-711, 736-746, 776-817 -- to martwy kod, który powinien zostać usunięty. Historia jest w Git.

3.2 Brak IDisposable / using pattern
MemoryManager otwiera handle procesu (OpenProcess) ale nigdy go nie zamyka -- resource leak
TcpClient, UdpClient, NetworkStream nie są poprawnie zwalniane
Serwer nie implementuje IDisposable
3.3 Puste bloki catch
Server.cs:272-276:

ServerForm.cs:157-161:

3.4 Zdarzenia wywoływane bez null-check
GameClient.cs:316 -- Initialized(InitializedType.Core) wywoływane bezpośrednio bez sprawdzenia null. Jeśli nikt nie subskrybuje eventu -- NullReferenceException.

3.5 static pole currentDriverNumber w Driver.cs:39
Statyczny licznik sterujący numerem kierowcy jest współdzielony między wszystkimi instancjami -- nie resetuje się po restarcie wyścigu, co prowadzi do błędnego mapowania power-upów.

3.6 ReadShort czyta 4 bajty zamiast 2
MemoryManager.cs:71 -- ReadBytes(address, 4) powinno być ReadBytes(address, 2) dla short (16-bitowa wartość).

4. Problemy bezpieczeństwa
4.1 Brak walidacji wejścia sieciowego
Serwer nigdy nie sprawdza długości nicknamu
Brak walidacji zakresu float (klient może wysłać NaN/Infinity)
Pakiety nie mają weryfikacji integralności ani wersjonowania
Brak limitu wielkości pakietu -- możliwy atak DoS
4.2 PROCESS_ALL_ACCESS zamiast minimalnych uprawnień
MemoryManager.cs:30 -- 0x1F0FFF to pełne uprawnienia do procesu. Powinno to być PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION -- minimalne wymagane uprawnienia.

4.3 VirtualAllocEx z PAGE_EXECUTE_READWRITE (0x40)
MemoryManager.cs:47 -- alokacja pamięci z pełnymi uprawnieniami RWX. Lepsza praktyka: alokuj jako RW, po zapisaniu zmień na RX.

5. Podsumowanie i priorytety napraw
Priorytet	Problem	Wpływ
P0	Race conditions na liście participants	Crash serwera pod obciążeniem
P0	Brak delimitacji pakietów TCP	Losowe uszkodzenie danych
P0	Współdzielony bufor bytes w kliencie	Uszkodzenie danych
P0	ReadShort czyta 4B zamiast 2B	Błędne dane z pamięci
P1	float.Parse zależny od locale	Crash na systemach nieanglojęzycznych
P1	Busy-wait loop w serwerze TCP	100% CPU
P1	Zapis tylko do Opponents[0]	Multiplayer >2 graczy nie działa
P1	Brak zamykania handle procesu	Resource leak
P2	20-parametrowy konstruktor	Trudność utrzymania kodu
P2	Zakomentowany kod (~300 linii)	Czytelność kodu
P2	Thread.Abort()	Niestabilność / niekompatybilność z .NET 5+
P3	PROCESS_ALL_ACCESS	Bezpieczeństwo
P3	Brak walidacji wejścia sieciowego	Stabilność serwera
Czy chcesz, żebym zaczął naprawiać te problemy? Mogę zacząć od P0 (krytycznych bugów) i iść w dół listy priorytetów.


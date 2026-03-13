The moment is here. LEGO Racers Online (not finished) and the LEGO Racers API (mostly finished, but needs new functions all the time) are now published on GitHub as an open source project.

I don't know how Git exactly works and the source code is published to the development branch.

 

I would love to see if you guys could contribute in this project and create forks so I can merge them into the master project. I will review all submitted code and if the code has a good quality, I will add it to the development branch (which had to be the master branch actually).

 

Don't ask me how to put this project Visual Studio, because I don't know how to. Please contact Grappigegovert for more information about this as he done this before.

 

One important thing to remember

The software is not complete yet and does probably not what you expects it to do! It's possible that it's not even running online functionality yet/anymore because of the latest developments. This can be fixed easily, but I didn't have time for this last weeks. I am not going to contribute much on the project too for the coming months due to school.

 

Requirements

    If you want to contribute to the master code, you will need a free GitHub account.
    Visual Studio (2013 if you don't want compatibility issues)
    A fresh brain because developing on this project is sometimes a pain in the ass

Credits

Me: Client, Server, Library and the API.

Grappigegovert: massive, massive research for the API. This guy created most of the API functions.

 

Repository

You can find all code on the following page:

 

https://github.com/Hoithebest/legoracersonline/tree/development

 

Remember to select the development branch, NOT the master branch!

 

Please create forks and submit them if you would like to contibute.

 

Information about the Solution

Some information about the Solution. All code is written in C#.

    Client
    Type: executable
    This Visual Studio Project contains the Client software which allows players to connect to the Server. It uses the LEGO Racers API to control the game. All game controlling functions should not be developed in this project but in the LEGORacersAPI project as this provides the core for communicating between C# and the LEGO Racers game process.
    LEGORacersAPI
    Type: class library (outputs a DLL)
    This Visual Studio Project is considered as the core of the whole project. In the beginning of this project, all game controlling functions were built into the Client directly, but for flexibility, the LEGO Racers API deserved an own Visual Studio Project so it could be reused in other projects too. The outputted DLL can be loaded into other projects to build mods for LEGO Racers.
    Library
    Type: class library (outputs a DLL)
    The library contains information that both the Client and the Server need to know about. Things like participants are stored here. The outputted DLL is being loaded in the Client and Server software.
    Server
    The Server software stores all data that the Clients sends to it. When Clients send data to the Server, it processes the requests and returns a response that the Client will process again. The Server is also capable of starting races (mostly).

Communication between Clients and Server

There are multiple requests a Client can do to the Server. This can be done using TCP and UDP. The following things are being sent through TCP:

    Connect to a Server
    When a player wants to join a Server, a request is sent to the Server, which checks if the entered username is still available. It sends a responsive whether the joining was successful or not.
    Start a race
    When the Server hoster starts a race, all participants (racers) receive a TCP packet containing the level information.
    Using a Power Up
    When a player uses a Power up, this is sent to the Server through TCP, which sends it to all other players in the game (TCP too).

For the requests being sent in a loop, we use the UDP protocol which is not as reliable as TCP is, but for those requests is doesn't care when a packet dies on its way to the Server or Client. The following things are being sent through UDP:

    Sending and receiving coordinates
    The current player coordinates are being sent with a very low delay. The Server stores this information and returns the coordinates of all the others players as a response. (Settings are also sent in these packets, but this has to change in the future.)

 

To-do list

The following things have a high priority in order to build further on the project:

    Starting a race from the Server
    Trigger the Initialized (in the API) correctly

I will add more to-do items later, as the list is much longer than mentioned above (can't think about more right now, it's been a while since I have been developing on the project).

 

When coding, please think of the following things:

    Put as many comments as possible in the code, ESPECIALLY WHEN WORKING ON THE API. CODE WITHOUT COMMENTS ABOVE FUNCTIONS WILL NOT BE ACCEPTED IN THE MASTER CODE!
    Work with as many classess as possible because this will make it easier for other developers and for extensibility.

If you need more information, please comment in this topic and I will add the answers here.

 

Happy coding friends!
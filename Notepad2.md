Basicly, your coordinates are being collected by the Client and sends them to the Server. The Server then replies with all the known coordinates of other players. When the Client receives this coordinates, its able to write them to the enemy players.

 

Currently, the only thing we do have is the following addresses:

 

Your player:

X, Y, Z, Rotation X and Rotation Y - with the following pointers and offsets:

 

Player base: "LEGORacers.exe"+000C67B0, with offsets: 518

 

From there you can retrieve the X, Y, Z, Rotation X and Rotation Y with the following offsets:

 

X: 0

Y: + 4

Z: + 8

Rotation X: - 24

Rotation Y: - 20

 

Enemy 1 (Champion):

Destination X, Destination Y, Destination Z, Rotation X, Rotation Y - with the following pointers and offsets:

 

Enemy 1 base: "LEGORacers.exe"+000C5258, with offsets: 794, 7a4, 4, 14, 514

 

X: 0

Y: + 4

Z: + 8

Rotation X: + 14 and + 18

Rotation Y: + 24 and + 28

 

But, the problem is that the Player Rotation X and Y are different values then the Enemy 1 Rotation X and Y, even when they are rotated the same.

 

If you are a Cheat Engine pro, please help us. Download the Cheat Table here with the coordinates (without the rotation values). Thanks

First off, >there are 3 different versions of LR1, so please post the version you are using.
The rotation is saved in 6 different floats (forward/up vector pair), with x, y and z directly after those.
I believe Xiron has made cheatengine files for 2 versions of LR, maybe you can check those out.

LegoRacers.exe:
    5683f89bfe8cb19e64ebd83d501f4f12
    987222 bytes
    25/04/2015 07:40:50

LegoRacers.icd:
    not found

LEGO.JAM:
    9ec6b4c3334d66a1563e8930f304701f
    19639466 bytes
    29/04/2015 18:43:43

GolDP.dll:
    e682063553de779579bf47b5e9bc5b1e
    413696 bytes
    12/01/2001 13:46:12

Oops, I forgot that I am a) having a patch version for versus mode without a controller and b) having a Rocket Racer Red Run mod for Circuit 7. I am going to replace the above files with the original ones and try again. I do know that the MD5 hash the Client copied to my clipboard is exactly that second line number under LegoRacers.exe.

Sorry, stupid mistake. I will try to pay more attention from now on. :)  Pretty nice tool, though.

I'll let you know about the problems I face next, and hope those are no stupid mistakes of me as well.

Checksum for the original game install:

LegoRacers.exe:
    325cbbedc9d745107bca4a8654fce4db
    987222 bytes
    12/01/2001 13:53:28

LegoRacers.icd:
    not found

LEGO.JAM:
    d1a82a99ea4962d6d84f691db5f93550
    19642024 bytes
    02/11/1999 10:32:36 --> thought this was edited since for the rerelease?

GolDP.dll:
    e682063553de779579bf47b5e9bc5b1e
    413696 bytes
    12/01/2001 13:46:12

When checking this thing;  it was interesting that when -novideo was not added to the shortcut, I got the .NET error again, when adding it, I get to indentify successfully.

So, server address?
You will need to run your own server.
When you build / start the server, you can connect with a client on the same computer by using localhost as server address.
Then, on a different computer, enter the local IP of the server if it's on the same network, or the public IP if it's not. (Although you will need to forward some ports when using the public IP)
To be honest, I don't even know if it will work, as I haven't tested it myself.

I will look into the .NET error when I have more time. (I'm a little busy with exams right now)

You need to open the .sln and compile it. 

Yep, It's been a while, but afaik starting the race from the mp client/server was never finished, so you have to start the same race manually on all clients, and then start the whole syncing multiplayer thing.
I believe I started rewriting a bunch of stuff in a fork of mine at one point, but I honestly have no idea in what sort of state that is  :P
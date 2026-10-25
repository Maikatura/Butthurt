
## THIS IS A NEW BRANCH!

I am remaking this whole program, so instead of just a console application, It will now have its own GUI. I am not 
planing to add support for some games that support addons without modding them. Here is a preview on how the new GUI 
will look like!

![](/img/preview.png)

## Welcome to Butthurt
So you wanna spice up your world of warcraft gameplay?<br>
Well why not try Butthurt?<br><br>
Connect your faviorite lovense toy and play world of warcraft like you never have done before!

### Setup

1. Install Intiface Central. https://intiface.com/central/ and press **Start Server**.
2. Turn on your toy and connect it in Intiface Central.
3. Start Butthurt (check code first or compile it yourself). Install the .NET runtime if it asks for it.
4. Go to the **World of Warcraft** page, pick your game folder (or press *Find it*) and press **Install**.
5. Press **Start** on the Home page, switch to WoW and `/reload`.
6. Type `/butthurt test` in game to feel it without having to die.

Enjoy your new style of playing World of Warcraft :3

### How it works

WoW addons can't talk to other programs, so the addon flashes a tiny square in the top-left corner of the game
in a different color per event (damage, heal, death, level up, achievement). Butthurt reads that pixel while WoW
is the active window and tells Intiface Central to vibrate. Which events are used and how strong they feel is set
in the app, so you never have to touch Lua. Use *Windowed (Fullscreen)* in WoW so the pixel can be read.

In game: `/butthurt toggle`, `/butthurt test [damage|heal|death|level_up|achievement]`, `/butthurt debug`.

### Todo list
- [x] Update addon to 12.0
- [x] Addon settings in app (events, intensity and duration are picked in the app).
- [x] Add addon install button. (copies the addon that ships with the app, nothing is downloaded)
- [x] Functional UI for Toys.
- [x] User guide how to setup and add new events (Guide page in the app).

#### Note!

I will not be able to finish the new UI for midnight release because this is not the most priority program I make. If you wanna use it on release with Midnight refer to the old console version that can be found [here](https://github.com/Maikatura/Butthurt).


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

In game: `/butthurt` opens the settings window (also in the AddOns menu by the minimap and in Options > AddOns), where you can turn events on or off and test them. Commands: `/butthurt toggle`, `/butthurt test [damage|heal|death|level_up|achievement]`, `/butthurt debug`.

### Twitch

The Twitch page turns your channel into haptics. Log in with Twitch (you approve it in your browser, Butthurt
never sees your password) and make rules for what should buzz and how hard:

- **Bits**: a range, e.g. 1–499 soft and 500+ strong, optionally longer for bigger cheers.
- **Subscriptions**: pick the tiers, e.g. only tier 2 and 3. Prime counts as tier 1. Resubs can be included or not.
- **Gifted subs**: amount range and tiers.
- **Channel point redeems**: by reward name, or every redeem (affiliate/partner only).
- **Follows**, **raids** (minimum viewers) and **chat commands** (e.g. `!buzz`, limited to subs, VIPs or mods, with a cooldown).

Every rule has its own intensity, duration, cooldown and a Test button.

**Twitch Client ID:** logging in needs a Twitch app. Register one at https://dev.twitch.tv/console/apps
(category *Application Integration*, client type *Public*, OAuth redirect URL `http://localhost`) and either paste its
Client ID on the Twitch page, or put it in `TwitchService.DefaultClientId` before building so streamers don't need
their own.

**Rewards made by rules:** tick *Let Butthurt create this reward* on any redeem rule (Twitch or VRChat) and set a
cost. Butthurt creates the reward on your channel, keeps its cost in sync, hides it while the rule (or its plugin)
is off and deletes it when you delete the rule. Rules naming the same reward share it.

**Channel point rewards:** the Twitch page can also create rewards by hand that belong to Butthurt. Their redemptions are
completed automatically, or refunded when nothing could react (for example VRChat wasn't running). Logins from
before this feature need to log out and in once to allow it.

### VRChat

The VRChat page lets Twitch change your avatar and your size over OSC (turn it on in VRChat: Action Menu →
Options → OSC). It needs the Twitch plugin turned on and logged in. Every action is a rule with the same Twitch
conditions as above, for example:

| When | Then |
| --- | --- |
| 20–49 bits | Grow ×1.1 for 30 seconds (stacks: two of them make ×1.21) |
| 50–299 bits | Switch to avatar A (optionally switch back after a while) |
| 300+ bits | Switch to avatar B |
| "Make me tiny" redeem | Shrink ×0.5 for 60 seconds |
| "Bigger boobies" redeem | Add 0.2 to the `BoobSize` parameter for 2 minutes (`Chest_Scale` on avatar B) |

**Avatar swaps take turns:** when someone redeems an avatar swap while another one is still running, it waits
in line. Each swap gets its full time, then the next one starts, and when nobody is waiting you go back to the avatar
you had before. The overlay shows the current swap counting down and who's up next; `{when}` in an announcement says
"now" or "in 25s".

**Pick lists:** Butthurt reads the avatar files VRChat writes when OSC is on
(`AppData\LocalLow\VRChat\VRChat\OSC`), so avatars can be picked by name and parameters from a searchable list
with their types, no typing IDs or parameter names by hand.

**Avatar parameters:** set or add to any avatar parameter (bool, int or float), for a while or until reset. Avatars
name things differently, so a rule can have a parameter name per avatar plus one for every other avatar. Everything
that is active (parameters and growth) is sent again after every avatar change, using the new avatar's names and
its own height, so a redeemed avatar swap doesn't undo it.

Growing sets `/avatar/eyeheight` (your normal height times every active multiplier, within the limits you set) and
puts you back when the effects run out. Size changes slide smoothly (1.5 seconds by default; set it to 0 on the VRChat
page to jump instantly). A new grow or shrink halfway through a slide keeps the motion going towards the new
size instead of stopping and starting again. Avatar changes use `/avatar/change` with the avatar's `avtr_…` ID.
Some worlds block scaling; avatar changes still work there.

### Stream overlay and announcements

Turn on **OBS overlay** on the VRChat page and add a Browser source in OBS with the address it shows
(`http://localhost:7878/` by default, 1920 × 1080, transparent). It shows the queue of running effects with
countdowns and an announcement every time a rule fires. Pick where each goes (announcements anywhere on a 3 × 3
grid, the queue in any corner) and how big it is right on the VRChat page; OBS updates live. Everything slides
and fades smoothly. For two sources placed differently, URL options win over the settings: `?queue=0` / `?alerts=0`,
`?alerts_at=bottom-left`, `?queue_at=top-right`, `?scale=1.5`.

Every VRChat rule has its own announcement you write yourself, with placeholders: `{user}` `{event}` `{bits}`
`{amount}` `{bits_min}` `{bits_max}` `{tier}` `{reward}` `{cost}` `{message}` `{multiplier}` `{size}` `{duration}`
`{avatar}` `{when}` `{wait}` `{parameter}` `{value}` `{rule}`. For example `{user} {bits} bits for {multiplier} growth` becomes
"X 50 bits for 1.1 growth". Announcements can also be posted in your Twitch chat (needs a fresh Twitch login once).

### Streaming mode (no toys)

Settings → Toys → **Toy features** turns off everything toy related: Intiface, the Toys page, the quick test,
Twitch's vibration rules and toy-only plugins like World of Warcraft. What's left is a streaming/VRChat tool.
Your toy settings are kept, turn it back on any time.

### Plugins

Every game or site is its own project, so they are easy to maintain and can be turned on and off on the
Settings page (a turned off plugin isn't loaded at all):

| Project | What it is |
| --- | --- |
| `Butthurt.Contracts` | The core: toys (Intiface), settings, theme, shared pages and the `IPlugin` interface |
| `Butthurt.Warcraft` | World of Warcraft: the corner pixel watcher, event settings and addon installer |
| `Butthurt.Twitch` | Twitch: login, EventSub, rules and channel point rewards |
| `Butthurt.VRChat` | VRChat: avatar changes, growing and avatar parameters over OSC, driven by Twitch rules |
| `Butthurt.Desktop` | The app window, registers the built-in plugins |

To add a game, copy one of the plugin projects, implement `IPlugin` and register it in `MainWindow`, or drop the
built DLL into the `Plugins` folder next to Butthurt.exe.

### Todo list
- [x] Update addon to 12.0
- [x] Addon settings in app (events, intensity and duration are picked in the app).
- [x] Add addon install button. (copies the addon that ships with the app, nothing is downloaded)
- [x] Functional UI for Toys.
- [x] User guide how to setup and add new events (Guide page in the app).
- [x] Twitch support (bits, subs per tier, gifts, redeems, follows, raids, chat commands).
- [x] Plugins that can be turned on and off.
- [x] VRChat: avatar changes and stacking growth from Twitch rules.
- [x] Butthurt-managed channel point rewards with auto fulfil/refund.
- [x] Rules create and delete their own Twitch rewards.
- [x] VRChat avatar parameters that survive avatar changes.
- [x] Smooth growing and shrinking.
- [x] Streaming mode: turn off every toy feature.
- [x] OBS overlay with the queue and custom announcements (also in Twitch chat).

### Making a release

Pushing a version tag builds Butthurt on GitHub and puts it on the Releases page. Normal commits don't trigger it.

```bash
git tag v0.2.0
git push origin v0.2.0
```

The release gets `Butthurt-v0.2.0-win-x64.zip` (no .NET install needed, the addon is included for the
install button) plus addon-only zips. The tag is also written into the addons' version, so the app knows
when an installed addon is out of date. Tags with a dash, like `v0.2.0-beta`, become pre-releases.

#### Note!

I will not be able to finish the new UI for midnight release because this is not the most priority program I make. If you wanna use it on release with Midnight refer to the old console version that can be found [here](https://github.com/Maikatura/Butthurt).

# What's new in GameRadar

English translation of `CHANGELOG.md` (the French file comes first): same versions, same dates, same parts (`### Improvements` for `### Évolutions`, `### Fixes` for `### Corrections`), as many lines (checked by the tests).
Shown by the app when it runs in English, and used for the English part of the GitHub release notes.

## 1.7.0 — 2026-10-16

### Improvements

- New "Free games" page, in the sidebar: the games Epic Games (this week's), Steam (with its free weekends, playable only during the offer) and Amazon Prime (with where to redeem them: GOG, Epic Games, Amazon Games…) give away right now, with the end of each offer and a button to go and claim them. Nothing is downloaded before the page is opened, and at most once a day.
- "My PS Plus subscription" also offers PlayStation Plus Essential: its games of the month show in "Free games" (Extra and Premium show them too).

### Fixes

- Steam, EA, Epic and PlayStation sync: when the network stops answering, it stops at once instead of waiting up to 30 seconds per game; the missing achievements and trophies are read at the next sync.
- PlayStation history: a game whose trophies could not be read yet no longer shows twice.

## 1.6.2 — 2026-10-14

### Improvements

- Covers: those not shown for 30 days are deleted at startup (games that left the catalogs, former full-size pictures): the data folder no longer grows forever.
- EA history: syncing takes a few seconds instead of about a minute (8 requests instead of 151, several at a time).
- "For you": the first computation, which looks up the Steam tags of the Game Pass games, takes about 2 minutes instead of 7.

### Fixes

- Settings › Data: a platform being synced says "Fetching…" until the end, even when a catalog arrives meanwhile; a catalog's error stays shown until its next download.
- "Add a game" no longer downloads the PS Plus catalog a second time while it is already downloading.
- Xbox, PlayStation and Epic accounts: signing out right while a sync renews the session no longer leaves the account connected until GameRadar is closed.

## 1.6.1 — 2026-10-13

### Improvements

- GameRadar is now open source (MIT license): the code and the downloads are together on github.com/Darvelize/game-radar. Each exe there is built by GitHub from this code, with an attestation that lets you check it.
- Catalogs: each catalog counts in "For you" and in the History as soon as it is downloaded: Steam, GOG and Epic no longer wait for the end of the Game Pass download.

### Fixes

- For you: a game sold on Epic or GOG and on the PlayStation Store (or in your PS Plus) now says "PlayStation and PC (Epic)" or "PlayStation and PC (GOG)", no longer just "PC (Epic)" or "PC (GOG)".
- PS Plus: choosing your plan while the app starts no longer downloads again a PS Plus catalog that is up to date.
- For you: "Wreckfest PlayStation®5 Version" and Wreckfest are now one game.

## 1.6.0 — 2026-10-12

### Improvements

- History: adds the games you played on Epic Games, with their play time and achievements (Settings › Accounts › Epic Games account). Epic offers no sign-in for apps: you paste once a code shown by epicgames.com, and the sign-in then stays encrypted on your computer.
- For you: also suggests games sold on Epic Games, among its top sellers, most played and top rated by its players, with their rating ("Epic" box). No Epic account needed; a click on the "Epic" badge opens the game's page.
- For you: also suggests the PS5 and PS4 top sellers of the PlayStation Store, with their players' rating, without the pre-orders ("PlayStation Store" box). The box is ticked by default if your history has PlayStation games; no account, and nothing is asked of the PlayStation Store while it is unticked. Click the "PlayStation" badge to open the game's page.
- Catalogs: Game Pass, PS Plus, Steam and GOG now update at the same time, at startup as with the button: the small catalogs no longer wait for the Game Pass download.
- For you and History: each game shows its 3 most voted Steam tags as badges ("#Souls-like", "#Third-Person Shooter", "#Online Co-Op"), more precise than the genres: on the cards instead of the genres, and under its name in the History.
- History: give Steam tags to your games, especially those Steam does not know (console exclusives, old games); the "Tags › Without tags" filter finds them, and the "+" badge under their name lets you pick them from Steam's list (a × removes those you added). They count in your taste and your recommendations like the most voted tags.
- Tags: a Steam game no longer sold (replaced by another edition, like Titan Quest by its Anniversary Edition) is now looked up by its title, and Titan Quest and Titan Quest Anniversary Edition are now one game.

## 1.5.1 — 2026-10-08

### Fixes

- GOG history: a game whose play time or last session GOG does not give no longer stops your profile from syncing.
- GOG catalog: a game just released, with no reviews yet, no longer stops the catalog from updating.
- EA history: a game whose achievements EA does not give no longer cancels the whole update; its achievements are asked again next time.
- EA history: some contents of EA's page, though copied correctly, were refused; they are now accepted.
- EA history: "Update my EA history" during a sync no longer runs EA a second time at once.
- History: unticking "All" in the platform filter no longer hides the games of a platform you add afterwards.

## 1.5.0 — 2026-10-08

### Improvements

- History: add the games you played in the EA app, EA Play included, with their play time and achievements (Settings › Accounts › EA account). EA offers no sign-in for apps: you copy the content of an EA page and paste it in GameRadar, which never saves it. Do it again whenever you want to update your EA history.
- History: add your GOG library in Settings › Accounts, with no sign-in nor key: just enter your GOG profile, which must be public. The play time and achievements GOG Galaxy followed count in your taste, and the GOG games you played are no longer recommended to you.
- For you: GOG's best sellers join the recommendations, even without an account, with a "GOG" box to show them or not. A game also sold on Steam or in your subscription shows only once, with a badge per store: click "GOG" to open its page. The GOG catalog is updated automatically every week.
- History: each filter (Platform, Status, Taste) starts with an "All" box that ticks or unticks everything in one click.

### Fixes

- History: a PlayStation game whose platinum you earned, but not the DLC trophies, now shows its "Story finished" flag ticked (it is finished according to PlayStation) instead of none.

## 1.4.0 — 2026-10-07

### Improvements

- Settings › Data: you see where GameRadar keeps your files, a button opens that folder, and a portable mode is explained step by step: create a "Data" folder next to GameRadar.exe and everything is kept there, ready to take on a USB stick.
- For you: the games on your Steam wishlist move up a little in your recommendations. The list is read together with your library, when you sync Steam.
- Add a game: the search now also finds the games of the Microsoft Store (Xbox) and the PlayStation Store, falling back on the PS Plus catalog (at the Premium level, whatever your plan) when the PlayStation Store does not answer: a PlayStation or Xbox exclusive can finally be found. A game found only on PlayStation keeps its genres and counts in your taste, and the platform fills itself in. The platforms are families (PlayStation, Xbox, Nintendo, PC other store), with Android and iOS added. The games you already added stay valid.
- At startup, GameRadar removes by itself the old cache files that are no longer useful.
- GameRadar.exe is twice as light: about 22 MB instead of 49.

### Fixes

- History: a game added by hand takes the color of its platform (PlayStation blue, Xbox green), like a synced game.
- "Erase all my data" is safer: a setting or a sign-in saved at the same moment no longer comes back after the erasure.

## 1.3.0 — 2026-10-05

### Improvements

- Game titles are now in English, the language of Steam: your PlayStation, Xbox and Game Pass games find their Steam tags far more often, and your recommendations get more precise. Your refused games and those added by hand are converted on their own.
- For you: click "Game Pass" or "Steam" on a card to open the game's page where you want to see it.
- First launch: a welcome screen explains in three steps how to connect your Xbox, Steam or PlayStation accounts from the Settings.
- For you shows right away at startup, even while the Game Pass catalog updates, and the filter responds faster.
- PS Plus: choose your plan (Extra or Premium) in Settings › Accounts, and For you suggests the games of the PS Plus catalog in your plan, as it does for the Game Pass, with a "PS Plus" box to show them or not. The catalog is only downloaded if you chose a plan.

### Fixes

- Your recommendations no longer stay stuck when the Microsoft Store or Steam does not answer, and a click on the filter or on "Not interested" during the computation is no longer lost.
- Without a Game Pass catalog (failed download), the Steam catalog still makes recommendations for you; an incomplete catalog is no longer saved in place of the good one.
- A game you already played is better left out of your recommendations, despite its edition ("Ultimate Edition", "— Remastered") or its platform.
- PlayStation and Xbox: an outage at Sony or an expired session no longer signs you out for nothing, and an interrupted PlayStation sync resumes where it stopped.
- "Erase all my data" erases your credentials first and lets nothing come back.
- Minor bug fixes and various optimizations (Steam, covers, adding games, settings, error log).

## 1.2.0 — 2026-10-02

### Improvements

- New: connect your PlayStation account (Settings › Accounts). Your trophies (PS3, PS4, PS5, Vita) and your play time (PS4, PS5) count in your taste; a platinum counts as a finished game.
- Your taste is understood far more finely, thanks to the Steam tags of all your games (even Xbox ones): "Open World", "Story Rich", "Souls-like"… rather than plain genres. The "Based on your taste" section says which ones count.
- Your hours of play count as much as your achievements: a game you spent a lot of time in counts in your taste, even if you didn't hunt its achievements.
- History: sort your games from A to Z, and filter them by platform, by status (mastered, finished, played) or by whether they count in your taste. Each platform now has its own color.

### Fixes

- A game you already played on one platform is no longer recommended in its edition for another ("Windows Edition", "Xbox One Edition").
- "Erase all my data" waits for your last choice to be saved, instead of risking writing it back right after the erasing.

## 1.1.0 — 2026-09-30

- Add by hand the games you played elsewhere (PlayStation, Switch, another PC store, an older console): History › "Add a game". They count in your taste, even without any Xbox or Steam account.
- A game added by hand is edited on its History line: platform, and how far you went.
- "Not interested": the button that shows when hovering a card rules out that game, and the games too much like it. "Undo" right away, or "Restore" in Settings › Preferences.
- The PC games from other stores that the Xbox app sees launched (no achievements nor play time) are no longer fetched.

## 1.0.1 — 2026-09-30

- Settings › About: the terms of use (one tab per language) and the licenses of GameRadar's open-source components.
- The "What's new" page finally shows its notes when GameRadar runs in English.
- If Microsoft ever cuts GameRadar's access to Xbox Live, a clear message explains it: your games already synced stay, and Steam keeps working.
- Pages now cross-fade; in Settings, the arrow keys move from one tab to the next.

## 1.0.0 — 2026-09-29

- First public version of GameRadar, in French and English (Settings › Preferences › Language).
- Xbox and Steam history merged: a game played on both has a single line.
- Recommendations based on how far you really went (achievements, 100% completions, playtime), with "Because you liked…" sections.
- Games compared by their style (the Steam players' tags), not just by their genre.
- Game Pass games of your plan and Steam store games, with a filter to show one, the other or both.
- At the top: the games leaving your Game Pass soon that look like what you enjoy.
- Click a card to open the game's page on the Xbox Store or on Steam.
- GameRadar tells you at startup when a new version is available.
- Everything stays on your computer: Settings › Privacy explains what GameRadar does with your data, where to remove its access to your accounts and how to erase everything.
- An error log: if something goes wrong, Settings › About › "Error log"; send today's file to the person who shared GameRadar with you.

# 🎯 GameRadar

**🇫🇷 Français** · [🇬🇧 English below](#english)

Des recommandations de jeux **d'après ce que tu as vraiment joué** : tes succès, tes platines, ton temps de jeu sur **Xbox**, **Steam**, **PlayStation**, **GOG**, **EA** et **Epic Games**. GameRadar te propose des jeux de **ton palier Game Pass** ou **PS Plus**, ou des catalogues **Steam**, **GOG**, **Epic** et **PlayStation Store**, qui devraient te plaire. En français et en anglais.

Le code est **open source** (licence [MIT](LICENSE)) : tu peux lire exactement ce que fait l'application, la compiler toi-même, ou vérifier que l'exe téléchargé vient bien de ce code.

## ⬇️ Télécharger

👉 [**Dernière version**](../../releases/latest) → télécharge `GameRadar.exe`.

- Windows 10 ou 11, 64 bits.
- Rien à installer : un seul fichier, à lancer directement.
- GameRadar t'avertit tout seul quand une nouvelle version sort.

### ✅ Vérifier l'exe

Chaque `GameRadar.exe` est construit par **GitHub Actions** à partir du code de ce dépôt ([`release.yml`](.github/workflows/release.yml)), jamais sur un PC, et reçoit une **attestation de provenance** signée par GitHub. Pour la vérifier avec [GitHub CLI](https://cli.github.com/) :

```
gh attestation verify GameRadar.exe -R Darvelize/game-radar
```

La release donne aussi l'empreinte SHA-256 du fichier (`Get-FileHash .\GameRadar.exe` dans PowerShell).

## 🛡️ Premier lancement : l'écran bleu de Windows

L'application n'est pas signée (un certificat coûte 150 à 300 $ par an, et ne supprimerait même pas cet avertissement tout de suite). Au premier lancement, Windows affiche donc **« Windows a protégé votre ordinateur »** :

1. clique sur **Informations complémentaires** ;
2. puis sur **Exécuter quand même**.

Ça ne se fait qu'une fois. Le premier démarrage est un peu plus lent : l'appli prépare ses fichiers.

## 🔌 Se connecter (Réglages › Comptes)

- **Xbox** : bouton « Se connecter avec Microsoft ». La page de connexion s'ouvre dans ton navigateur : GameRadar ne voit jamais ton mot de passe. Microsoft y indique « éditeur non vérifié » : c'est normal pour un projet perso (la vérification est réservée aux entreprises), et GameRadar ne se sert de cet accès que pour lire ton historique de jeux. Juste en dessous, choisis ton palier **Game Pass** (Essential, Premium, Ultimate ou PC Game Pass).
- **Steam** (si tu y joues) : colle l'adresse de ton profil et ta clé API personnelle.
  - Ton profil doit montrer ses jeux : Steam › Modifier le profil › Confidentialité › « Détails des jeux » : **Public**.
  - La clé s'obtient sur [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey) (nom de domaine : `localhost`). Garde-la pour toi.
- **PlayStation** (si tu y joues) : connecte-toi sur playstation.com, ouvre la page de ton jeton depuis l'appli et colle ce qu'elle affiche. Ce jeton ouvre ta session Sony : garde-le pour toi. GameRadar le garde chiffré sur ton PC.
- **EA** (si tu joues dans l'appli EA, EA Play compris) : connecte-toi sur ea.com, ouvre « la page EA » depuis l'appli, copie tout ce qu'elle affiche (Ctrl+A, Ctrl+C) et colle-le. Ce jeton vaut 4 heures et n'est jamais enregistré : recolle-le quand tu veux mettre ton historique EA à jour.
- **Epic Games** (si tu y joues) : connecte-toi sur epicgames.com, ouvre la page du code depuis l'appli et colle-le, une seule fois. La connexion reste ensuite chiffrée sur ton PC.
- **GOG** (si tu y joues) : indique le nom de ton profil (ou son adresse gog.com/u/…). Ton profil doit être public : dans les réglages de confidentialité de ton compte GOG, rends-le visible par tout le monde. Aucune connexion ni clé.
- Pas de compte, ou un jeu joué ailleurs (Switch…) ? Ajoute-le à la main : **Historique › « Ajouter un jeu »**.

## 🧠 Comment il choisit

- **Ta progression compte, pas le lancement** : un jeu fini, platiné ou poncé des dizaines d'heures pèse lourd ; un jeu lancé 10 minutes ne compte pas.
- **Il compare le style des jeux**, grâce aux tags des joueurs Steam (« looter-shooter », « souls-like »…). Aimer *Warframe* mène vers *Borderlands* ou *The Division 2*, pas vers *Call of Duty*.
- **Des sections « Parce que tu as aimé X »**, et en tête **les jeux qui quittent bientôt ton Game Pass**. Un clic sur un badge de plateforme ouvre la page du jeu.
- Dans l'**Historique**, tu peux cocher « Histoire finie », retirer un jeu de tes goûts (un platine fait juste pour le gamerscore, par exemple) ou lui ajouter des tags.

## 🔒 Tes données

Tout reste **sur ton PC**, dans `%LOCALAPPDATA%\GameRadar` : sessions chiffrées par Windows, caches, réglages. GameRadar n'a aucun serveur et ne partage rien. **Réglages › Confidentialité** explique le détail, donne les liens pour retirer son accès à tes comptes, et un bouton pour tout effacer.

## 🐞 Un souci ?

**Réglages › À propos › « Journal des erreurs »** ouvre le dossier du journal (sans aucun secret : ni mot de passe, ni jeton, ni clé). Envoie le fichier du jour avec ta description, ou ouvre une [issue](../../issues).

## 🛠️ Compiler

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
dotnet run --project src/GameRadar.App
dotnet publish src/GameRadar.App -p:PublishProfile=win-x64   # → publish/GameRadar.exe
```

- `src/GameRadar.Core` : toute la logique (un dossier par plateforme, le moteur de recommandation dans `Services/`), sans interface.
- `src/GameRadar.App` : l'application [Avalonia](https://avaloniaui.net/) (MVVM).
- `tests/` : tests xUnit, sans réseau ni données réelles.

## 📜 Licence et conditions

- Le code est sous licence [**MIT**](LICENSE).
- En utilisant l'application, tu acceptes ses [**conditions d'utilisation**](legal/LICENSE.txt) : services tiers, **fournie telle quelle, sans garantie**.
- GameRadar contient des composants open source (Avalonia, .NET, SkiaSharp…) : leurs licences sont dans [THIRD-PARTY-NOTICES.txt](legal/THIRD-PARTY-NOTICES.txt).
- Les textes sont aussi dans l'appli : **Réglages › À propos**.

## ⚠️ À savoir

- Windows uniquement.
- GameRadar utilise des services non documentés (historique Xbox Live, PlayStation Network, appli EA, lanceur Epic, profil et catalogue GOG, catalogues Game Pass, PS Plus, Epic et PlayStation Store) qui peuvent changer sans prévenir.
- GameRadar est un projet indépendant, non affilié à Microsoft Corporation, Valve Corporation, Sony Interactive Entertainment, GOG sp. z o.o., Electronic Arts Inc. ni Epic Games, Inc., et approuvé par aucune d'elles. Xbox et Game Pass sont des marques de Microsoft Corporation ; Steam est une marque de Valve Corporation ; PlayStation et PlayStation Plus sont des marques de Sony Interactive Entertainment ; GOG et GOG.com sont des marques de GOG sp. z o.o. ; EA et EA Play sont des marques d'Electronic Arts Inc. ; Epic Games et Epic Games Store sont des marques d'Epic Games, Inc. Les jaquettes et noms des jeux appartiennent à leurs éditeurs.

---

<a id="english"></a>
## 🇬🇧 English

Game recommendations **based on what you really played**: your achievements, your 100% completions, your playtime on **Xbox**, **Steam**, **PlayStation**, **GOG**, **EA** and **Epic Games**. GameRadar suggests games from **your Game Pass** or **PS Plus plan**, or from the **Steam**, **GOG**, **Epic** and **PlayStation Store** catalogs, that you should enjoy. In English and French.

The code is **open source** ([MIT](LICENSE) license): you can read exactly what the app does, build it yourself, or check that the downloaded exe really comes from this code.

### ⬇️ Download

👉 [**Latest version**](../../releases/latest) → download `GameRadar.exe`.

- Windows 10 or 11, 64-bit.
- Nothing to install: a single file, run it directly.
- GameRadar tells you by itself when a new version comes out.

#### ✅ Check the exe

Each `GameRadar.exe` is built by **GitHub Actions** from the code of this repository ([`release.yml`](.github/workflows/release.yml)), never on a PC, and gets a **provenance attestation** signed by GitHub. To check it with [GitHub CLI](https://cli.github.com/):

```
gh attestation verify GameRadar.exe -R Darvelize/game-radar
```

The release also gives the SHA-256 hash of the file (`Get-FileHash .\GameRadar.exe` in PowerShell).

### 🛡️ First launch: the blue Windows screen

The app is not signed (a certificate costs $150 to $300 a year, and would not even remove this warning right away). On first launch, Windows shows **"Windows protected your PC"**:

1. click **More info**;
2. then **Run anyway**.

Only once. The first start is a bit slower: the app prepares its files.

### 🔌 Signing in (Settings › Accounts)

- **Xbox**: "Sign in with Microsoft" button. The sign-in page opens in your browser: GameRadar never sees your password. Microsoft shows "unverified publisher" there: normal for a personal project (verification is only open to companies), and GameRadar only uses this access to read your game history. Right below, choose your **Game Pass** plan (Essential, Premium, Ultimate or PC Game Pass).
- **Steam** (if you play there): paste your profile address and your personal API key.
  - Your profile must show its games: Steam › Edit Profile › Privacy Settings › "Game details": **Public**.
  - Get the key at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey) (domain name: `localhost`). Keep it to yourself.
- **PlayStation** (if you play there): sign in on playstation.com, open the page of your token from the app and paste what it shows. This token opens your Sony session: keep it to yourself. GameRadar keeps it encrypted on your PC.
- **EA** (if you play in the EA app, EA Play included): sign in to ea.com, open "the EA page" from the app, copy everything it shows (Ctrl+A, Ctrl+C) and paste it. This token lasts 4 hours and is never saved: paste it again whenever you want to update your EA history.
- **Epic Games** (if you play there): sign in on epicgames.com, open the code page from the app and paste the code, only once. The sign-in then stays encrypted on your PC.
- **GOG** (if you play there): enter your profile name (or its address gog.com/u/…). Your profile must be public: in the privacy settings of your GOG account, make it visible to everyone. No sign-in nor key.
- No account, or a game played elsewhere (Switch…)? Add it by hand: **History › "Add a game"**.
- To switch the language: Settings › Preferences › Language.

### 🧠 How it picks

- **Your progress counts, not launching a game**: a game finished, completed at 100% or played for dozens of hours weighs a lot; a game launched for 10 minutes does not count.
- **It compares the style of games**, using the Steam players' tags ("looter shooter", "souls-like"…). Liking *Warframe* leads to *Borderlands* or *The Division 2*, not to *Call of Duty*.
- **"Because you liked X" sections**, and at the top **the games leaving your Game Pass soon**. A click on a platform badge opens the game's page.
- In the **History**, you can tick "Story finished", remove a game from your taste (a 100% done just for the gamerscore, for example) or add tags to it.

### 🔒 Your data

Everything stays **on your PC**, in `%LOCALAPPDATA%\GameRadar`: sessions encrypted by Windows, caches, settings. GameRadar has no server and shares nothing. **Settings › Privacy** gives the details, the links to remove its access to your accounts, and a button to erase everything.

### 🐞 Something wrong?

**Settings › About › "Error log"** opens the log folder (free of any secret: no password, token or key). Send today's file with your description, or open an [issue](../../issues).

### 🛠️ Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
dotnet run --project src/GameRadar.App
dotnet publish src/GameRadar.App -p:PublishProfile=win-x64   # → publish/GameRadar.exe
```

- `src/GameRadar.Core`: all the logic (one folder per platform, the recommendation engine in `Services/`), no UI.
- `src/GameRadar.App`: the [Avalonia](https://avaloniaui.net/) app (MVVM).
- `tests/`: xUnit tests, no network nor real data.

### 📜 License and terms

- The code is under the [**MIT**](LICENSE) license.
- By using the app, you accept its [**terms of use**](legal/LICENSE.txt): third-party services, **provided as is, without warranty**.
- GameRadar contains open-source components (Avalonia, .NET, SkiaSharp…): their licenses are in [THIRD-PARTY-NOTICES.txt](legal/THIRD-PARTY-NOTICES.txt).
- The texts are also in the app: **Settings › About**.

### ⚠️ Good to know

- Windows only.
- GameRadar uses undocumented services (Xbox Live history, PlayStation Network, EA app, Epic launcher, GOG profile and catalog, Game Pass, PS Plus, Epic and PlayStation Store catalogs) that may change without notice.
- GameRadar is an independent project, not affiliated with nor endorsed by Microsoft Corporation, Valve Corporation, Sony Interactive Entertainment, GOG sp. z o.o., Electronic Arts Inc. or Epic Games, Inc. Xbox and Game Pass are trademarks of Microsoft Corporation; Steam is a trademark of Valve Corporation; PlayStation and PlayStation Plus are trademarks of Sony Interactive Entertainment; GOG and GOG.com are trademarks of GOG sp. z o.o.; EA and EA Play are trademarks of Electronic Arts Inc.; Epic Games and Epic Games Store are trademarks of Epic Games, Inc. Game covers and names belong to their publishers.

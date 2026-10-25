# Nouveautés de GameRadar

Source unique des notes de version : l'appli affiche ce fichier (page « Nouveautés »), et les notes des releases GitHub en sont extraites.
Format lu par `Changelog.Parse` : `## <version>` (suivi de ` — <AAAA-MM-JJ>` une fois publiée), puis les parties `### Évolutions` et `### Corrections` (depuis la 1.2.0), chacune avec une ligne `- ` par nouveauté, en français, écrite pour les joueurs.
Traduction anglaise : `CHANGELOG.en.md` (`### Improvements`, `### Fixes`), mêmes versions, mêmes dates, mêmes parties, autant de lignes (vérifié par les tests).

## 1.7.2 — 2026-10-25

### Corrections

- Comptes : connecter un autre compte pendant que la plateforme se synchronise récupère bien l'historique du nouveau compte, une fois la synchronisation en cours terminée (avant, il fallait la relancer à la main).
- « Pour toi » : un jeu commencé sans temps de jeu enregistré, mais avec des succès (joué hors ligne, sur EA ou Epic), n'est plus recommandé comme un jeu jamais lancé.
- Catalogues Game Pass et PS Plus : quand un abonnement disparaît vraiment ou est fusionné avec un autre, le catalogue n'est plus bloqué à son ancienne version : il est accepté après quatre semaines de refus.
- « Ajouter un jeu » : une nouvelle recherche fonctionne même quand la précédente attend encore Steam ; ses résultats ne s'affichent plus sous le nouveau texte.
- Historique : changer la plateforme d'un jeu ajouté à la main (par exemple vers PlayStation), ou le retirer, met aussitôt à jour la case « PlayStation Store » et le nombre de tes jeux au Game Pass.
- EA : un refus d'EA sur la liste publique des jeux ne fait plus oublier le jeton que tu viens de coller, et un jeu dont EA n'a pas pu donner les succès est redemandé à la synchronisation suivante au lieu de rester sans succès.
- Les titres comme « Grounded - PC » ou « Halo Infinite - Xbox One » ne gardent plus un tiret à la fin.
- « Mes jeux » : un jeu Xbox vendu au Game Pass sous un autre nom (« Sea of Thieves 2024 Edition ») n'a plus deux cartes ; cochée seule, la source « Mes jeux » le reste après une première synchronisation PlayStation.
- Tes goûts : les tags Steam sont cherchés pour les jeux que tu as cochés « Compte dans mes goûts », et plus pour ceux que tu as décochés.
- Catalogue Epic : un jeu marqué seulement « Co-op » n'est plus pris pour un jeu de coopération en ligne (il peut n'être qu'en local).
- Bandeau : une synchronisation ou un catalogue qui réussit à nouveau en retire sa propre erreur précédente ; un lien ou un dossier que Windows n'arrive pas à ouvrir le dit au lieu de ne rien faire ; une erreur inattendue au démarrage est notée dans le journal et signalée.
- Historique Xbox : une réponse incomplète de Xbox Live n'efface plus ton historique Xbox (il était remplacé par une liste vide jusqu'à la synchronisation suivante).
- Historique : la ligne dont tu viens de décocher le cœur ou « Histoire finie » ne disparaît plus quand un filtre est actif, et l'arrivée des catalogues Steam, GOG ou Epic ne replie plus la ligne dépliée.
- Historique : le résumé compte comme terminés les mêmes jeux que le filtre « Terminé » (jeux ajoutés à la main au niveau « Terminé », jeux terminés selon PlayStation).
- « Jeux gratuits » : une jaquette qui n'a pas pu se charger est retentée à la visite suivante ; l'erreur de Steam parle de ses jeux gratuits ; une source sans abonnement n'affiche plus son ancienne erreur à côté de la phrase d'aide.
- « Pour toi » : quand Steam ne répond pas, le calcul n'attend plus la liste des noms de tags à chaque clic ; le libellé « Catalogue » de la barre latérale suit tous tes catalogues, plus seulement le Game Pass ; « hier » au lieu de « il y a 0 jours » le jour du passage à l'heure d'hiver.

## 1.7.1 — 2026-10-19

### Évolutions

- La page « Jeux gratuits » se range en deux onglets : « Définitif » pour les jeux offerts, qui restent à toi une fois récupérés, et « Temporaire » pour ceux qu'on ne peut jouer gratuitement que quelques jours, comme les week-ends gratuits de Steam.
- Nouveau dans « Temporaire » : les jours de jeu gratuit (Free Play Days) du Game Pass, du jeudi au dimanche, si tu as choisi ton abonnement Game Pass dans Réglages › Comptes.
- « Jeux gratuits » : un jeu déjà dans ton historique, sur n'importe quelle plateforme, le dit sur sa carte (« Dans ton historique »).
- « Pour toi » : les cases des sources passent dans un menu « Sources », avec le nombre de jeux de chacune ; sa dernière phrase ouvre directement Réglages › Comptes, où se choisit ton abonnement.
- Nouvelle source « Mes jeux » dans ce menu : tes jeux pas encore terminés (jamais lancés ou commencés, qui ne comptent pas dans tes goûts) te sont proposés selon tes goûts, avec où tu en es ; la cocher décoche les autres sources, pour ne voir qu'eux. Décochée au départ.

## 1.7.0 — 2026-10-16

### Évolutions

- Nouvelle page « Jeux gratuits », dans la barre latérale : les jeux offerts en ce moment par Epic Games (ceux de la semaine), par Steam (avec ses week-ends gratuits, jouables seulement pendant l'offre) et par Amazon Prime (avec l'endroit où les récupérer : GOG, Epic Games, Amazon Games…), avec la fin de chaque offre et un bouton pour aller les récupérer. Rien n'est téléchargé avant d'ouvrir la page, et au plus une fois par jour.
- « Mon abonnement PS Plus » propose aussi PlayStation Plus Essential : ses jeux du mois apparaissent dans « Jeux gratuits » (Extra et Premium les montrent aussi).

### Corrections

- Synchronisation Steam, EA, Epic et PlayStation : quand le réseau ne répond plus, elle s'arrête aussitôt au lieu d'attendre jusqu'à 30 secondes par jeu ; les succès et trophées qui manquent sont lus à la synchronisation suivante.
- Historique PlayStation : un jeu dont les trophées n'ont pas encore pu être lus n'apparaît plus deux fois.

## 1.6.2 — 2026-10-14

### Évolutions

- Jaquettes : celles qui n'ont pas été affichées depuis 30 jours sont supprimées au démarrage (jeux sortis des catalogues, anciennes images en grand format) : le dossier des données ne grossit plus sans fin.
- Historique EA : la synchronisation prend quelques secondes au lieu d'environ une minute (8 requêtes au lieu de 151, plusieurs à la fois).
- « Pour toi » : le premier calcul, qui cherche les tags Steam des jeux du Game Pass, prend environ 2 minutes au lieu de 7.

### Corrections

- Réglages › Données : une plateforme en cours de synchronisation affiche « Récupération… » jusqu'au bout, même quand un catalogue arrive pendant ce temps ; l'erreur d'un catalogue reste affichée jusqu'à son prochain téléchargement.
- « Ajouter un jeu » ne télécharge plus le catalogue PS Plus une deuxième fois quand il est déjà en cours de téléchargement.
- Comptes Xbox, PlayStation et Epic : se déconnecter pile pendant qu'une synchronisation renouvelle la session ne laisse plus le compte connecté jusqu'à la fermeture de GameRadar.

## 1.6.1 — 2026-10-13

### Évolutions

- GameRadar est maintenant open source (licence MIT) : le code et les téléchargements sont réunis sur github.com/Darvelize/game-radar. Chaque exe y est construit par GitHub à partir de ce code, avec une attestation qui permet de le vérifier.
- Catalogues : chaque catalogue compte dans « Pour toi » et dans l'Historique dès qu'il est téléchargé : Steam, GOG et Epic n'attendent plus la fin du téléchargement du Game Pass.

### Corrections

- Pour toi : un jeu vendu sur Epic ou GOG et au PlayStation Store (ou dans ton PS Plus) indique maintenant « PlayStation et PC (Epic) » ou « PlayStation et PC (GOG) », et non plus seulement « PC (Epic) » ou « PC (GOG) ».
- PS Plus : choisir ton palier pendant le démarrage de l'appli ne retélécharge plus un catalogue PS Plus déjà à jour.
- Pour toi : « Wreckfest PlayStation®5 Version » et Wreckfest ne font plus qu'un seul jeu.

## 1.6.0 — 2026-10-12

### Évolutions

- Historique : ajoute les jeux auxquels tu as joué sur Epic Games, avec leur temps de jeu et leurs succès (Réglages › Comptes › Compte Epic Games). Epic ne propose pas de connexion aux applis : tu colles une seule fois un code affiché par epicgames.com, et la connexion reste ensuite chiffrée sur ton ordinateur.
- Pour toi : propose aussi des jeux vendus sur Epic Games, parmi ses meilleures ventes, ses jeux les plus joués et les mieux notés par ses joueurs, avec leur note (case « Epic »). Sans compte Epic ; un clic sur le badge « Epic » ouvre la page du jeu.
- Pour toi : propose aussi les meilleures ventes PS5 et PS4 du PlayStation Store, avec la note de ses joueurs, sans les précommandes (case « PlayStation Store »). La case est cochée d'office si tu as des jeux PlayStation dans ton historique ; sans compte, et rien n'est demandé au PlayStation Store tant qu'elle est décochée. Un clic sur le badge « PlayStation » ouvre la page du jeu.
- Catalogues : le Game Pass, PS Plus, Steam et GOG se mettent maintenant à jour en même temps, au démarrage comme avec le bouton : les petits catalogues n'attendent plus le téléchargement du Game Pass.
- Pour toi et Historique : chaque jeu montre ses 3 tags Steam les plus votés en badges (« #Soulslike », « #Tir à la 3ᵉ personne », « #Coop en ligne »), plus précis que les genres : sur les cartes à la place des genres, et sous son nom dans l'Historique.
- Historique : donne des tags Steam à tes jeux, surtout ceux que Steam ne connaît pas (exclusivités console, jeux anciens) ; le filtre « Tags › Sans tags » les trouve, et le badge « + » sous leur nom permet de les choisir dans la liste de Steam (un × retire ceux que tu as ajoutés). Ils comptent dans tes goûts et tes recommandations comme les tags les plus votés.
- Tags : un jeu Steam qui n'est plus en vente (remplacé par une autre édition, comme Titan Quest par son Anniversary Edition) est maintenant cherché par son titre, et Titan Quest et Titan Quest Anniversary Edition ne font plus qu'un seul jeu.

## 1.5.1 — 2026-10-08

### Corrections

- Historique GOG : un jeu dont GOG ne donne pas le temps de jeu ou la dernière partie n'empêche plus la synchronisation de ton profil.
- Catalogue GOG : un jeu tout juste sorti, encore sans avis, n'empêche plus la mise à jour du catalogue.
- Historique EA : un jeu dont EA ne donne pas les succès n'annule plus toute la mise à jour ; ses succès sont redemandés la fois suivante.
- Historique EA : certains contenus de la page d'EA, pourtant bien copiés, étaient refusés ; ils sont maintenant acceptés.
- Historique EA : « Mettre à jour mon historique EA » pendant une synchronisation ne lance plus EA une deuxième fois en même temps.
- Historique : décocher « Toutes » dans le filtre des plateformes ne masque plus les jeux d'une plateforme que tu ajoutes ensuite.

## 1.5.0 — 2026-10-08

### Évolutions

- Historique : ajoute les jeux auxquels tu as joué dans l'appli EA, EA Play compris, avec leur temps de jeu et leurs succès (Réglages › Comptes › Compte EA). EA ne propose pas de connexion aux applis : tu copies le contenu d'une page d'EA et tu le colles dans GameRadar, qui ne l'enregistre jamais. À refaire quand tu veux mettre ton historique EA à jour.
- Historique : ajoute ta bibliothèque GOG dans Réglages › Comptes, sans connexion ni clé : indique simplement ton profil GOG, qui doit être public. Le temps de jeu et les succès suivis par GOG Galaxy comptent dans tes goûts, et les jeux GOG auxquels tu as joué ne te sont plus recommandés.
- Pour toi : les meilleures ventes de GOG s'ajoutent aux recommandations, même sans compte, avec une case « GOG » pour les afficher ou non. Un jeu aussi vendu sur Steam ou présent dans ton abonnement n'apparaît qu'une fois, avec un badge par boutique : clique sur « GOG » pour ouvrir sa page. Le catalogue GOG est mis à jour automatiquement chaque semaine.
- Historique : chaque filtre (Plateforme, Statut, Goûts) commence par une case « Toutes » / « Tous » qui coche ou décoche tout d'un clic.

### Corrections

- Historique : un jeu PlayStation dont tu as la platine, mais pas les trophées des DLC, montre maintenant son drapeau « Histoire finie » coché (il est terminé d'après PlayStation) au lieu de n'en avoir aucun.

## 1.4.0 — 2026-10-07

### Évolutions

- Réglages › Données : tu vois où GameRadar range tes fichiers, un bouton ouvre ce dossier, et un mode portable est expliqué pas à pas : crée un dossier « Data » à côté de GameRadar.exe et tout y est rangé, prêt à être emporté sur une clé USB.
- Pour toi : les jeux de ta liste de souhaits Steam remontent un peu dans tes recommandations. La liste est lue en même temps que ta bibliothèque, quand tu synchronises Steam.
- Ajouter un jeu : la recherche trouve aussi les jeux du Microsoft Store (Xbox) et du PlayStation Store, avec un repli sur le catalogue PS Plus (au niveau Premium, quel que soit ton abonnement) quand le PlayStation Store ne répond pas : une exclusivité PlayStation ou Xbox se retrouve enfin. Un jeu trouvé seulement sur PlayStation garde ses genres et pèse dans tes goûts, et la plateforme se remplit toute seule. Les plateformes sont des familles (PlayStation, Xbox, Nintendo, PC autre boutique), avec Android et iOS en plus. Tes jeux déjà ajoutés restent valides.
- Au démarrage, GameRadar supprime tout seul les anciens fichiers de cache devenus inutiles.
- GameRadar.exe est deux fois plus léger : environ 22 Mo au lieu de 49.

### Corrections

- Historique : un jeu ajouté à la main prend la couleur de sa plateforme (PlayStation en bleu, Xbox en vert), comme un jeu synchronisé.
- « Effacer toutes mes données » est plus sûr : un réglage ou une connexion enregistrés au même moment ne reviennent plus après l'effacement.

## 1.3.0 — 2026-10-05

### Évolutions

- Les titres des jeux sont maintenant en anglais, la langue de Steam : tes jeux PlayStation, Xbox et Game Pass retrouvent bien plus souvent leurs tags Steam, et tes recommandations gagnent en précision. Tes jeux refusés et ceux ajoutés à la main sont convertis tout seuls.
- Pour toi : clique sur « Game Pass » ou « Steam » sur une carte pour ouvrir la page du jeu là où tu veux le voir.
- Premier démarrage : un écran d'accueil explique en trois étapes comment connecter tes comptes Xbox, Steam ou PlayStation depuis les Réglages.
- Pour toi s'affiche tout de suite au démarrage, même pendant la mise à jour du catalogue Game Pass, et le filtre réagit plus vite.
- PS Plus : choisis ton abonnement (Extra ou Premium) dans Réglages › Comptes, et Pour toi te propose les jeux du catalogue PS Plus de ton palier, comme pour le Game Pass, avec une case « PS Plus » pour les afficher ou non. Le catalogue n'est téléchargé que si tu as choisi un abonnement.

### Corrections

- Tes recommandations ne restent plus bloquées quand le Microsoft Store ou Steam ne répond pas, et un clic sur le filtre ou sur « Pas intéressé » pendant le calcul n'est plus perdu.
- Sans catalogue Game Pass (téléchargement raté), le catalogue Steam te fait quand même des recommandations ; un catalogue incomplet n'est plus enregistré à la place du bon.
- Un jeu que tu as déjà joué est mieux écarté de tes recommandations, malgré son édition (« Ultimate Edition », « — Remastered ») ou sa plateforme.
- PlayStation et Xbox : une panne chez Sony ou une session expirée ne te déconnecte plus pour rien, et une synchro PlayStation interrompue reprend là où elle s'était arrêtée.
- « Effacer toutes mes données » efface tes identifiants en premier et ne laisse plus rien revenir.
- Corrections de bugs mineurs et optimisations diverses (Steam, jaquettes, ajout de jeux, réglages, journal des erreurs).

## 1.2.0 — 2026-10-02

### Évolutions

- Nouveau : connecte ton compte PlayStation (Réglages › Comptes). Tes trophées (PS3, PS4, PS5, Vita) et ton temps de jeu (PS4, PS5) comptent dans tes goûts ; une platine compte comme un jeu terminé.
- Tes goûts sont compris bien plus finement, grâce aux tags Steam de tous tes jeux (même Xbox) : « Monde ouvert », « Scénario riche », « Souls-like »… plutôt que de simples genres. La section « D'après tes goûts » dit lesquels comptent.
- Tes heures de jeu comptent autant que tes succès : un jeu où tu as passé beaucoup de temps compte dans tes goûts, même si tu n'as pas chassé ses succès.
- Historique : trie tes jeux de A à Z, et filtre-les par plateforme, par statut (platiné, terminé, joué) ou selon qu'ils comptent dans tes goûts. Chaque plateforme a maintenant sa couleur.

### Corrections

- Un jeu déjà joué sur une plateforme ne t'est plus recommandé dans son édition pour une autre (« Windows Edition », « Xbox One Edition »).
- « Effacer toutes mes données » attend que ton dernier choix soit enregistré, au lieu de risquer de le réécrire juste après l'effacement.

## 1.1.0 — 2026-09-30

- Ajoute à la main les jeux joués ailleurs (PlayStation, Switch, autre boutique PC, ancienne console) : Historique › « Ajouter un jeu ». Ils comptent dans tes goûts, même sans compte Xbox ni Steam.
- Un jeu ajouté à la main se modifie sur sa ligne de l'Historique : plateforme, et jusqu'où tu es allé.
- « Pas intéressé » : le bouton qui apparaît au survol d'une carte écarte ce jeu, et ceux qui lui ressemblent trop. « Annuler » tout de suite, ou « Remettre » dans Réglages › Préférences.
- Les jeux PC d'autres boutiques que l'appli Xbox voit se lancer (sans succès ni temps de jeu) ne sont plus récupérés.

## 1.0.1 — 2026-09-30

- Réglages › À propos : les conditions d'utilisation (un onglet par langue) et les licences des composants open source de GameRadar.
- La page « Nouveautés » affiche enfin ses notes quand GameRadar est en anglais.
- Si Microsoft coupe un jour l'accès de GameRadar à Xbox Live, un message clair l'explique : tes jeux déjà synchronisés restent, et Steam continue de marcher.
- Les pages s'enchaînent en fondu ; dans les Réglages, les flèches du clavier passent d'un onglet à l'autre.

## 1.0.0 — 2026-09-29

- Première version publique de GameRadar, en français et en anglais (Réglages › Préférences › Langue).
- Historique Xbox et Steam fusionné : un jeu joué sur les deux n'a qu'une ligne.
- Recommandations d'après ta progression réelle (succès, platines, temps de jeu), avec des sections « Parce que tu as aimé… ».
- Comparaison des jeux par leur style (les tags des joueurs Steam), pas seulement par leur genre.
- Jeux du Game Pass selon ton palier et jeux de la boutique Steam, avec un filtre pour afficher l'un, l'autre ou les deux.
- En tête : les jeux qui quittent bientôt ton Game Pass et qui te ressemblent.
- Clique sur une carte pour ouvrir la page du jeu sur le Store Xbox ou sur Steam.
- GameRadar t'avertit au lancement quand une nouvelle version est disponible.
- Tout reste sur ton ordinateur : Réglages › Confidentialité explique ce que GameRadar fait de tes données, où retirer son accès à tes comptes et comment tout effacer.
- Un journal des erreurs : en cas de souci, Réglages › À propos › « Journal des erreurs » ; envoie le fichier du jour à la personne qui t'a partagé GameRadar.

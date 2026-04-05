# HDR Profile Switcher

Utilitaire Windows qui applique automatiquement le bon profil ICC couleur (SDR/HDR) à chaque écran connecté.

## Le problème

Windows 11 ne permet pas de changer facilement de profil ICC par écran ni d'appliquer automatiquement le bon profil quand on passe de SDR à HDR (ou vice-versa). Le profil doit être changé manuellement à chaque fois dans Paramètres → Affichage → Profil de couleurs.

## La solution

HDR Profile Switcher tourne en tray (zone de notification) et :
- Détecte automatiquement les écrans connectés et leur état HDR/SDR
- Applique le profil ICC configuré pour chaque écran selon son mode
- Surveille les changements d'état (branchement TV, activation/désactivation HDR)
- Permet de configurer les profils via une interface graphique (clic droit → Configuration)

## Configuration requise

- Windows 10 version 2004 (20H1) ou ultérieur
- GPU avec driver WDDM 2.6+ (NVIDIA Pascal+, AMD RX 400+, Intel Gen10+)
- Profils ICC installés dans `C:\Windows\System32\spool\drivers\color\`

## Installation

1. Téléchargez la dernière release
2. Placez `HdrProfileSwitcher.exe` et `config.json` dans un dossier de votre choix
3. Lancez `HdrProfileSwitcher.exe`
4. Clic droit sur l'icône tray → Configuration pour associer vos profils

## Utilisation

L'icône dans la zone de notification affiche :
- La lettre de l'écran principal détecté
- L'état SDR ou HDR

Clic droit pour accéder au menu :
- **Configuration** : associer profils SDR/HDR par écran
- **Forcer le profil** : appliquer manuellement un profil
- **Ouvrir le log** : voir les actions effectuées
- **Quitter** : fermer l'application

## Comment ça marche

L'application utilise l'API Windows `ColorProfileAddDisplayAssociation` avec le LUID de chaque adaptateur graphique pour changer le profil ICC par défaut. Cette API est la méthode officielle Microsoft pour gérer les profils couleur sur Windows 10/11.

## Licence

MIT

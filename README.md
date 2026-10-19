# 🧰 WinTools 1.0 — Barra multifunzione a comparsa per Windows 10 e 11

**WinTools** è una barra che compare quando porti il mouse sul bordo dello schermo (o con un tasto rapido): dentro ci sono i **tasti personalizzabili** per i programmi, le cartelle e gli strumenti che usi ogni giorno e una **ricerca istantanea** di file, cartelle e app. Interfaccia in **stile Windows 11** (Fluent: tema chiaro/scuro automatico, colore di accento di sistema, Mica nelle finestre), come [WinOptima](https://github.com/brn78/WinOptima).

**[⬇ Scarica WinTools.exe](https://github.com/brn78/WinTools/releases/latest/download/WinTools.exe)** · **[Pagina del progetto](https://brn78.github.io/WinTools/)**

> Windows potrebbe mostrare «Windows ha protetto il PC»: l'exe non è firmato digitalmente. Clicca **Ulteriori informazioni → Esegui comunque**.

Non richiede installazione né dipendenze: **`WinTools.exe` è un unico file da circa 320 KB** (usa .NET Framework 4.8, già incluso in Windows 10 e 11). Copialo in una cartella stabile (es. `C:\Strumenti`) e avvialo: niente permessi di amministratore.

---

## 🚀 Come si usa

* **Porta il mouse sul bordo superiore dello schermo**: la barra scende; quando il mouse se ne va, si ritira. Oppure premi **Ctrl+Alt+W** (configurabile) o fai clic sull'icona nell'area di notifica.
* **Cerca**: scrivi nella casella e i risultati compaiono mentre digiti. Frecce per scegliere, **Invio** apre, **Ctrl+Invio** apre la cartella che contiene il file, **Shift+Invio** avvia come amministratore, **Esc** chiude. In fondo ci sono sempre «Esegui» (come Win+R) e «Cerca sul web».
* **Tasti**: clic per aprire, **trascina** per riordinare, tasto destro per modificare o rimuovere, **+** per aggiungerne uno. **Trascina un file o una cartella sulla barra** e diventa un tasto. Un **contenitore** raccoglie più tasti in un menu: trascinaci sopra i tasti che vuoi.
* **Spillo**: blocca la barra sempre visibile. **Ingranaggio**: impostazioni.
* **Area di notifica**: clic sinistro mostra la barra, clic destro apre il menu (blocca, impostazioni, aggiorna l'indice, avvio con Windows, aggiornamenti, esci).

## 🆕 Versione 1.1.3
* **Correzione**: le icone di file e cartelle nei risultati (e dei tasti con icona del file) erano capovolte.

## 🆕 Versione 1.1.2
* **Menu del tasto destro con le icone** (risultati, tasti della barra, contenitori) e sottomenu «Sposta in» che si apre davvero, con la freccia.

## 🆕 Versione 1.1.1
* **Trascinamento rifatto**: il tasto trascinato segue il mouse come «fantasma», una linea mostra dove finirà, il contenitore sotto il mouse si evidenzia. Funziona tra barra e menu dei contenitori in entrambe le direzioni e per riordinare dentro un contenitore. Non usa più il drag&drop di Windows (che restava a volte senza risposta); i file da Esplora file si trascinano come prima.

## 🆕 Versione 1.1.0
* **Contenitori di tasti**: un tasto che, al clic, apre un menu con i tasti che ci metti dentro. Si crea con **+ → tasto destro → Aggiungi un contenitore**, dall'editor (interruttore *Contenitore di tasti*) o dalle impostazioni; i tasti ci finiscono **trascinandoli sopra** al contenitore (o nel suo menu aperto), con *Sposta in* dal tasto destro, oppure con *Nuovo contenitore…* che lo crea già con quel tasto dentro. Dal menu del contenitore: tasto destro per modificare, spostare sulla barra o rimuovere. Rimuovendo un contenitore i suoi tasti tornano sulla barra.
* **Recenti**: con la casella vuota, **Canc** toglie il recente selezionato; dal tasto destro *Rimuovi dai recenti* e *Svuota i recenti*; anche da *Impostazioni → Ricerca e indice*.
* **Avvio di Windows con indice ritardato**: l'indice salvato è disponibile subito, la rilettura completa parte dopo i minuti impostati (3 di base, *Impostazioni → Ricerca e indice*) per non appesantire l'accensione del PC.
* **Più spazio ai tasti**: via l'icona accanto alla ricerca.

## 🆕 Versione 1.0.1
* **Correzione**: con la barra comparsa dal bordo non si riusciva a scrivere nella ricerca (la finestra non prendeva il fuoco e i tasti finivano nell'app di prima). Ora il primo clic sulla barra la attiva e porta il cursore nella casella.

## 🆕 Versione 1.0

* **Barra a comparsa** dal bordo superiore o inferiore, su qualunque schermo (quello con il mouse o il principale), con animazione, angoli arrotondati e ombra. Si ritira da sola o resta bloccata.
* **Tasti personalizzabili e trascinabili**: programmi, file, cartelle, indirizzi web, `ms-settings:` e `shell:`; etichetta, argomenti, cartella di lavoro, avvio come amministratore; icona del file, uno degli 80 simboli Fluent o un'immagine. Un catalogo di **60 strumenti di Windows** pronti (Gestione dispositivi, Servizi, Visualizzatore eventi, Connessioni di rete, Firewall, Terminale, Pannello di controllo, Cestino, Blocca il PC…), come i collegamenti rapidi di WinOptima.
* **Indicizzazione e ricerca veloce** (le idee dell'indice dell'archivio di [BlackHole Workspaces](https://github.com/brn78/BlackHole-Workspaces)): i nomi di file e cartelle delle unità locali vengono letti in sottofondo con priorità bassa, tenuti in memoria in una struttura compatta (circa 55 byte per voce: 600.000 voci ≈ 35 MB) e salvati in `index.bin`, così al prossimo avvio la ricerca è pronta subito. Un `FileSystemWatcher` per unità segue le modifiche; ogni tanto (12 ore) si rilegge tutto per sicurezza. La ricerca scorre il buffer «ripiegato» (minuscolo, senza accenti) in pochi millisecondi: tutte le parole, in qualunque ordine, nel nome oppure nel percorso; prima chi si chiama proprio così, poi gli inizi di parola, le cartelle, i file più recenti e quelli che apri più spesso. Con parole scritte male propone «forse cercavi».
* **App del menu Start** (desktop e Store) tra i risultati, in cima quando il nome corrisponde: si leggono da `shell:AppsFolder`, la stessa cartella virtuale del menu Start.
* **Avvio con Windows** (voce nell'avvio dell'utente corrente, aggiornata se sposti l'exe) e **persistenza** di tasti e preferenze in `%LOCALAPPDATA%\WinTools\WinTools.settings.json` (JSON leggibile).
* **Aggiornamenti automatici**: all'avvio e ogni 6 ore controlla le release su GitHub; se c'è una versione nuova la scarica, ne verifica dimensione e SHA-256 e la installa alla chiusura, oppure subito con *Riavvia e aggiorna*. La versione precedente viene eliminata da sola. Si disattiva da *Impostazioni → Informazioni*.
* **Una sola istanza**: un secondo avvio (o un collegamento) fa comparire la barra.

---

## 🔍 Che cosa indicizza

* Di base tutte le **unità fisse** (C:\, D:\…), saltando file e cartelle nascosti o di sistema (quindi anche `AppData`), `%SystemRoot%`, `Windows.old`, `$Recycle.Bin`, `node_modules`, `.git`, le cache e qualche altra cartella di servizio. Tutto si cambia da *Impostazioni → Ricerca e indice*: cartelle di partenza, cartelle da saltare (un nome salta tutte quelle con quel nome, un percorso solo quello), file nascosti sì/no.
* Solo i **nomi**: il contenuto dei file non viene mai letto. Le unità di rete e i supporti rimovibili non si indicizzano (si possono aggiungere a mano come cartelle).
* L'indice non richiede l'amministratore: legge le cartelle con i permessi dell'utente.

## 🔒 Privacy e sicurezza

* Nessun dato lascia il PC. L'unica connessione è verso `api.github.com` per il controllo degli aggiornamenti (disattivabile).
* L'app gira **senza privilegi**: i programmi avviati dai tasti non partono elevati, a meno che non lo chieda il tasto («Avvia come amministratore», con la normale richiesta UAC).
* Per rimuovere tutto: cancella `WinTools.exe` e la cartella `%LOCALAPPDATA%\WinTools`; la voce di avvio si toglie da *Impostazioni → Generale* (o da Gestione attività → App di avvio).

---

## 🗂️ Contenuto della cartella

| File | Scopo |
|---|---|
| `WinTools.exe` | **L'unico file da distribuire**: si genera con `Build.ps1` e si pubblica solo nelle [release](https://github.com/brn78/WinTools/releases) |
| `Publish.ps1` | Crea la release su GitHub con l'exe e tiene solo le ultime 2 |
| `Build.ps1` | Disegna l'icona (WPF → .ico multi-risoluzione) e compila con `dotnet build` |
| `WinTools.csproj`, `app.manifest` | Progetto (.NET Framework 4.8, WPF) e manifest (DPI per monitor, nessuna elevazione) |
| `src/` | Sorgenti C#: `App` (tray, tasto rapido, bordo), `UI/BarWindow` (la barra), `UI/SettingsWindow`, `UI/ButtonEditorWindow`, `UI/Presets` (strumenti di Windows e glifi), `Search/FileIndex` (indice), `Search/AppCatalog` (app del menu Start), `Search/SearchText` e `Scoring` (normalizzazione e pertinenza), `Updater`, `Settings`, `Theme`, `Native` |
| `docs/` | Pagina del progetto (GitHub Pages) |

In `%LOCALAPPDATA%\WinTools` l'app crea `WinTools.settings.json` (preferenze e tasti), `index.bin` (l'indice), `update\` (aggiornamenti scaricati) e, in caso di problemi, `WinTools-error.log`.

### Compilare

Serve l'[SDK .NET](https://dotnet.microsoft.com/download) (qualunque versione recente): il targeting pack di .NET Framework 4.8 lo scarica da solo se manca.

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```

### Pubblicare una nuova versione

1. Aumenta `<Version>` in `WinTools.csproj` (es. `1.1.0`): è l'unica fonte della versione.
2. Fai commit e push.
3. `powershell -ExecutionPolicy Bypass -File .\Publish.ps1 -Notes "Cosa cambia"`: compila, crea la release `v1.1.0` con l'exe ed elimina quelle più vecchie, tenendo le ultime 2. Le copie in uso si aggiornano da sole.

---

## ℹ️ Note tecniche

* **Requisiti**: Windows 10 o 11 con .NET Framework 4.8 (incluso da Windows 10 1903 in poi). Mica richiede Windows 11 22H2 o successivo; sulle versioni precedenti le finestre hanno uno sfondo a tinta unita.
* La barra è una finestra WPF senza bordo, sempre in primo piano, che non compare in Alt+Tab; la parte trasparente lascia passare i clic.
* L'indice usa `FindFirstFileEx` con recupero a blocchi grandi su più thread in modalità «in sottofondo» (`THREAD_MODE_BACKGROUND_BEGIN`): la lettura di 600.000 voci richiede una ventina di secondi su SSD senza rallentare il resto.
* Le icone di file, cartelle e app arrivano da `IShellItemImageFactory` su un thread dedicato e vengono messe in cache.

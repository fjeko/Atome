# Fabrique l'icone de l'application (src\Revisions.App\Ressources\icone.ico) a partir du logo.
#
#   powershell -ExecutionPolicy Bypass -File outils\faire-icone.ps1
#
# Un fichier .ico contient plusieurs tailles, et Windows choisit la plus proche de ce qu'il affiche.
# Un logo detaille devient illisible en petit : on fournit donc trois niveaux de detail.
#   - 16 a 40 px (barre de titre, barre des taches) : dessin tres simple, 3 noeuds ;
#   - 48 a 256 px (icones du Bureau)                : dessin a traits epais, 5 noeuds comme le logo.
# Le logo d'origine (logo-source.webp) sert de modele ; il est trop fin pour une icone.
# (Pas d'accents dans ce fichier : Windows PowerShell 5.1 les lit mal dans un script sans BOM.)
param([string]$Planche = "")   # chemin facultatif d'une image de controle montrant toutes les tailles

Add-Type -AssemblyName PresentationCore, WindowsBase
$ressources = Join-Path $PSScriptRoot "..\src\Revisions.App\Ressources"
$source = Join-Path $ressources "logo-source.webp"

# ---- Logo d'origine, sans son bord blanc dechire ----
$flux = [IO.File]::OpenRead($source)
$logo = [Windows.Media.Imaging.BitmapDecoder]::Create($flux, 'PreservePixelFormat', 'OnLoad').Frames[0]
$flux.Close()
$logoRecadre = New-Object Windows.Media.Imaging.CroppedBitmap($logo, (New-Object Windows.Int32Rect(56, 56, 1142, 1142)))

# ---- Couleurs reprises du logo ----
function Pinceau([byte]$r, [byte]$g, [byte]$b) { $p = New-Object Windows.Media.SolidColorBrush([Windows.Media.Color]::FromRgb($r, $g, $b)); $p.Freeze(); $p }
$marine = Pinceau 0x14 0x1E 0x33
$cuivre = Pinceau 0xD9 0x93 0x62
$creme  = Pinceau 0xEF 0xE4 0xD4

function Point($x, $y) { New-Object Windows.Point($x, $y) }
function Trait($epaisseur) { $t = New-Object Windows.Media.Pen($cuivre, $epaisseur); $t.StartLineCap = 'Round'; $t.EndLineCap = 'Round'; $t }

# ---- Les trois dessins. Chacun recoit le contexte de dessin et la taille en pixels. ----

# Logo d'origine, avec des coins arrondis.
$dessinLogo = { param($dc, $taille)
    $cadre = New-Object Windows.Rect(0, 0, $taille, $taille)
    $dc.PushClip((New-Object Windows.Media.RectangleGeometry($cadre, ($taille * 0.18), ($taille * 0.18))))
    $dc.DrawImage($logoRecadre, $cadre)
    $dc.Pop()
}

# Dessin moyen, grille de 64 : arbre de competences (7 noeuds, pleins = acquis, anneaux = a debloquer).
$dessinMoyen = { param($dc, $taille)
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($taille / 64.0), ($taille / 64.0))))
    $dc.DrawRoundedRectangle($marine, $null, (New-Object Windows.Rect(0, 0, 64, 64)), 12, 12)
    $lien = Trait 3.2
    $dc.DrawLine($lien, (Point 32 54), (Point 32 41))
    $dc.DrawLine($lien, (Point 32 41), (Point 18 29))
    $dc.DrawLine($lien, (Point 18 29), (Point 18 14))
    $dc.DrawLine($lien, (Point 32 41), (Point 46 29))
    $dc.DrawLine($lien, (Point 46 29), (Point 46 14))
    $dc.DrawLine($lien, (Point 32 41), (Point 32 14))
    $anneau = New-Object Windows.Media.Pen($cuivre, 2.6)
    foreach ($n in @(@(32, 54), @(32, 41), @(18, 29), @(18, 11))) { $dc.DrawEllipse($cuivre, $null, (Point $n[0] $n[1]), 4.6, 4.6) }
    foreach ($n in @(@(46, 29), @(46, 11), @(32, 11))) { $dc.DrawEllipse($marine, $anneau, (Point $n[0] $n[1]), 4.6, 4.6) }
    $dc.Pop()
}

# Dessin simple, grille de 32 : version epuree (5 noeuds) pour les petites tailles.
$dessinSimple = { param($dc, $taille)
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($taille / 32.0), ($taille / 32.0))))
    $dc.DrawRoundedRectangle($marine, $null, (New-Object Windows.Rect(0, 0, 32, 32)), 6, 6)
    $lien = Trait 2.6
    $dc.DrawLine($lien, (Point 16 26), (Point 16 18))
    $dc.DrawLine($lien, (Point 16 18), (Point 8 10))
    $dc.DrawLine($lien, (Point 16 18), (Point 24 10))
    $dc.DrawLine($lien, (Point 16 18), (Point 16 7))
    foreach ($n in @(@(16, 26), @(16, 18), @(8, 8), @(24, 8), @(16, 6))) { $dc.DrawEllipse($cuivre, $null, (Point $n[0] $n[1]), 3.4, 3.4) }
    $dc.Pop()
}

# ---- Quelle taille utilise quel dessin ----
# Attention : pour toute taille au-dessus de 48 px, l'Explorateur Windows part de l'image de 256 px
# et la reduit. Elle doit donc etre a traits epais elle aussi, sinon l'icone du Bureau est illisible
# sur un ecran a haute resolution. ($dessinLogo reste disponible si on veut revenir au logo detaille.)
$plan = @(
    @(256, $dessinMoyen), @(128, $dessinMoyen),
    @(96, $dessinMoyen), @(72, $dessinMoyen), @(64, $dessinMoyen), @(48, $dessinMoyen),
    @(40, $dessinSimple), @(32, $dessinSimple), @(24, $dessinSimple), @(20, $dessinSimple), @(16, $dessinSimple))

function EnPng([int]$taille, [scriptblock]$dessin) {
    $visuel = New-Object Windows.Media.DrawingVisual
    $dc = $visuel.RenderOpen(); & $dessin $dc $taille; $dc.Close()
    $image = New-Object Windows.Media.Imaging.RenderTargetBitmap($taille, $taille, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($visuel)
    $codeur = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $codeur.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $memoire = New-Object IO.MemoryStream; $codeur.Save($memoire)
    , $memoire.ToArray()
}
$images = $plan | ForEach-Object { , (EnPng $_[0] $_[1]) }

# ---- Ecriture du .ico : un en-tete, une ligne de 16 octets par taille, puis les images PNG ----
$memoire = New-Object IO.MemoryStream
$e = New-Object IO.BinaryWriter($memoire)
$e.Write([uint16]0); $e.Write([uint16]1); $e.Write([uint16]$plan.Count)
$position = 6 + 16 * $plan.Count
for ($i = 0; $i -lt $plan.Count; $i++) {
    $cote = [byte]($plan[$i][0] % 256)          # 256 s'ecrit 0 dans le format .ico
    $e.Write($cote); $e.Write($cote); $e.Write([byte]0); $e.Write([byte]0)
    $e.Write([uint16]1); $e.Write([uint16]32)
    $e.Write([uint32]$images[$i].Length); $e.Write([uint32]$position)
    $position += $images[$i].Length
}
foreach ($image in $images) { $e.Write($image) }
$e.Flush()
[IO.File]::WriteAllBytes((Join-Path $ressources "icone.ico"), $memoire.ToArray())
[IO.File]::WriteAllBytes((Join-Path $ressources "icone-256.png"), $images[0])
Write-Host "Icone ecrite : $($plan.Count) tailles, $($memoire.Length) octets"

# ---- Planche de controle facultative : chaque taille (sauf 256), en vraie grandeur ----
if ($Planche) {
    $visuel = New-Object Windows.Media.DrawingVisual
    $dc = $visuel.RenderOpen()
    $dc.DrawRectangle((Pinceau 0x2B 0x2B 0x2B), $null, (New-Object Windows.Rect(0, 0, 450, 150)))
    $dc.DrawRectangle((Pinceau 0xE8 0xE8 0xE8), $null, (New-Object Windows.Rect(450, 0, 450, 150)))
    foreach ($decalage in 0, 450) {
        $x = $decalage + 10
        for ($i = 1; $i -lt $plan.Count; $i++) {
            $taille = $plan[$i][0]
            $bitmap = New-Object Windows.Media.Imaging.BitmapImage
            $bitmap.BeginInit(); $bitmap.StreamSource = (New-Object IO.MemoryStream(, $images[$i])); $bitmap.EndInit()
            if ($x + $taille -le $decalage + 445) { $dc.DrawImage($bitmap, (New-Object Windows.Rect($x, (140 - $taille), $taille, $taille))) }
            $x += $taille + 8
        }
    }
    $dc.Close()
    $image = New-Object Windows.Media.Imaging.RenderTargetBitmap(900, 150, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($visuel)
    $codeur = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $codeur.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $fichier = [IO.File]::Create($Planche); $codeur.Save($fichier); $fichier.Close()
}

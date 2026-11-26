namespace Revisions.Core.Voix;

/// <summary>
/// Un fichier à télécharger pour la voix naturelle : son nom, sa taille exacte, son empreinte SHA-256
/// et les adresses où le trouver (dans l'ordre : la Release d'Atome d'abord, puis la source d'origine).
/// </summary>
public sealed record FichierVoix(string Nom, long Taille, string Sha256, IReadOnlyList<string> Adresses)
{
    /// <summary>Vrai pour l'archive du moteur, qu'il faut décompresser après vérification.</summary>
    public bool EstArchive => Nom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Timbre d'une voix : sert à donner une voix différente à chaque personnage d'un dialogue.</summary>
public enum Timbre { Feminin, Masculin }

/// <summary>Une voix Piper du paquet : son nom affiché, son modèle (.onnx, avec son .onnx.json à côté) et son timbre.</summary>
public sealed record VoixPiper(string Id, string Nom, string Modele, Timbre Timbre);

/// <summary>
/// Un « paquet » de voix : le moteur Piper et ses modèles de voix, téléchargés une seule fois
/// dans un dossier qui porte <see cref="Version"/> (changer de voix = nouvelle version, nouveau dossier).
/// La première voix est celle par défaut (phrases isolées, exemples).
/// </summary>
public sealed record PaquetVoix(string Version, string NomAffiche, string Langue, IReadOnlyList<VoixPiper> Voix, IReadOnlyList<FichierVoix> Fichiers)
{
    /// <summary>Taille totale à télécharger, en octets.</summary>
    public long TailleTotale => Fichiers.Sum(f => f.Taille);

    /// <summary>Voix d'un timbre donné (la voix par défaut si le paquet n'en a pas).</summary>
    public VoixPiper VoixDe(Timbre timbre) => Voix.FirstOrDefault(v => v.Timbre == timbre) ?? Voix[0];
}

/// <summary>
/// Les voix anglaises livrées avec Atome. Choix et licences détaillés dans THIRD-PARTY-NOTICES.md :
/// <list type="bullet">
/// <item>moteur : rhasspy/piper 2023.11.14-2 pour Windows (code MIT ; il contient espeak-ng, sous GPL-3.0,
/// lancé comme programme séparé, ce qui laisse Atome sous licence MIT) ;</item>
/// <item>voix féminine : « cori » (high, anglais britannique) et voix masculine : « norman » (medium, anglais américain),
/// toutes deux entraînées de zéro sur des enregistrements LibriVox du domaine public.</item>
/// </list>
/// Les voix plus récentes (« Piper 1.5 », diphtongues en plusieurs caractères) ne marchent pas avec ce moteur :
/// celles-ci ont été choisies parce qu'elles sont au format d'origine.
/// </summary>
public static class CatalogueVoix
{
    /// <summary>Release GitHub d'Atome qui héberge les fichiers (créée une fois pour toutes).</summary>
    public const string Release = "https://github.com/fjeko/Atome/releases/download/voix-piper-1/";

    /// <summary>Version figée du dépôt Hugging Face des voix Piper (une adresse qui ne change jamais).</summary>
    private const string HuggingFace = "https://huggingface.co/rhasspy/piper-voices/resolve/5ec43b186ef915a195d3ddbcfd9916e76c503c3f/en/";

    public static PaquetVoix Piper1 { get; } = new(
        Version: "piper-1",
        NomAffiche: "Cori et Norman (voix naturelles Piper)",
        Langue: "en-GB",
        Voix:
        [
            new("cori", "Cori (anglais britannique)", "en_GB-cori-high.onnx", Timbre.Feminin),
            new("norman", "Norman (anglais américain)", "en_US-norman-medium.onnx", Timbre.Masculin),
        ],
        Fichiers:
        [
            new("piper_windows_amd64.zip", 22_477_236,
                "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea",
                [Release + "piper_windows_amd64.zip", "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"]),
            new("en_GB-cori-high.onnx", 114_219_352,
                "470b4dd634c98f8a4850d7626ffc3dfc90774628eeef6605a6dd8f88f30a5903",
                [Release + "en_GB-cori-high.onnx", HuggingFace + "en_GB/cori/high/en_GB-cori-high.onnx"]),
            new("en_GB-cori-high.onnx.json", 4_963,
                "9e7fb5b5671612c22f3c81cbe46c1ae87b031a4632bcb509e499dad6f1e2adec",
                [Release + "en_GB-cori-high.onnx.json", HuggingFace + "en_GB/cori/high/en_GB-cori-high.onnx.json"]),
            new("en_US-norman-medium.onnx", 63_531_379,
                "b9739443232a80a59c7d18810dd856899bf16a7964725f5ab81ea49b1351cb71",
                [Release + "en_US-norman-medium.onnx", HuggingFace + "en_US/norman/medium/en_US-norman-medium.onnx"]),
            new("en_US-norman-medium.onnx.json", 4_968,
                "6c2db7f558a4a8deb9fe822583c1c5105f6c4e834dd0f9de8ad17a888ee9fe1d",
                [Release + "en_US-norman-medium.onnx.json", HuggingFace + "en_US/norman/medium/en_US-norman-medium.onnx.json"]),
        ]);
}

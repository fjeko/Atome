namespace Revisions.Data;

/// <summary>
/// Scripts de création et d'évolution du schéma, dans l'ordre.
/// Règle : on n'édite jamais un script déjà livré, on en ajoute un nouveau à la fin.
/// </summary>
internal static class Migrations
{
    public static readonly string[] Scripts =
    [
        // ---- 1 : schéma initial ----------------------------------------------------------
        """
        CREATE TABLE matiere (
          id    INTEGER PRIMARY KEY,
          nom   TEXT NOT NULL UNIQUE,
          bloc  TEXT
        );

        CREATE TABLE pack (
          id              TEXT PRIMARY KEY,          -- identifiant stable du pack
          matiere_id      INTEGER NOT NULL REFERENCES matiere(id),
          partie          TEXT NOT NULL,
          sequence        TEXT,
          seance          TEXT NOT NULL,
          ordre           INTEGER,
          source_fichier  TEXT NOT NULL,             -- relatif au dossier Cours
          source_pages    INTEGER,
          resume          TEXT NOT NULL,
          notions_json    TEXT NOT NULL,
          format_version  INTEGER NOT NULL,
          chemin_fichier  TEXT,                      -- dernier fichier importé
          empreinte       TEXT NOT NULL,             -- SHA-256 du fichier : réimport ignoré si inchangé
          date_import     TEXT NOT NULL,
          date_maj        TEXT NOT NULL,
          date_lecture    TEXT                       -- première ouverture en lecture
        );

        CREATE TABLE section (
          id            TEXT PRIMARY KEY,
          pack_id       TEXT NOT NULL REFERENCES pack(id),
          position      INTEGER NOT NULL,
          titre         TEXT NOT NULL,
          contenu       TEXT NOT NULL,               -- Markdown + LaTeX
          page_source   INTEGER,
          notions_json  TEXT,
          archivee      INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE carte (
          id            TEXT PRIMARY KEY,            -- unique dans toute la base
          pack_id       TEXT NOT NULL REFERENCES pack(id),
          section_id    TEXT NOT NULL REFERENCES section(id),
          position      INTEGER NOT NULL,
          type          TEXT NOT NULL CHECK (type IN ('question','qcm','exercice')),
          difficulte    INTEGER NOT NULL CHECK (difficulte BETWEEN 1 AND 3),
          donnees_json  TEXT NOT NULL,               -- champs propres au type de carte
          archivee      INTEGER NOT NULL DEFAULT 0,  -- carte disparue du pack : gardée pour l'historique
          date_ajout    TEXT NOT NULL
        );

        CREATE TABLE etat_carte (                    -- état FSRS courant, une ligne par carte
          carte_id           TEXT PRIMARY KEY REFERENCES carte(id),
          stabilite          REAL,                   -- NULL = carte jamais vue
          difficulte_fsrs    REAL,
          echeance           TEXT,                   -- jour de révision (AAAA-MM-JJ)
          derniere_revision  TEXT,
          repetitions        INTEGER NOT NULL DEFAULT 0,
          oublis             INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE session_revision (
          id     INTEGER PRIMARY KEY,
          debut  TEXT NOT NULL,
          fin    TEXT,
          mode   TEXT NOT NULL,                      -- quotidienne | reprise | seance | point_faible | bonus
          jour   TEXT NOT NULL
        );

        CREATE TABLE revision (                      -- historique : jamais modifié ni supprimé
          id                INTEGER PRIMARY KEY,
          carte_id          TEXT NOT NULL REFERENCES carte(id),
          session_id        INTEGER REFERENCES session_revision(id),
          date_heure        TEXT NOT NULL,
          jour              TEXT NOT NULL,
          note              INTEGER NOT NULL CHECK (note BETWEEN 1 AND 4),
          duree_ms          INTEGER,
          etait_due         INTEGER NOT NULL,
          retrievabilite    REAL,
          stabilite_avant   REAL,
          stabilite_apres   REAL,
          difficulte_avant  REAL,
          difficulte_apres  REAL,
          xp                INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE profil (                        -- une seule ligne
          id        INTEGER PRIMARY KEY CHECK (id = 1),
          xp_total  INTEGER NOT NULL DEFAULT 0       -- ne baisse jamais
        );
        INSERT INTO profil (id, xp_total) VALUES (1, 0);

        CREATE TABLE reglage (
          cle     TEXT PRIMARY KEY,
          valeur  TEXT NOT NULL
        );

        CREATE TABLE journal_import (
          id             INTEGER PRIMARY KEY,
          date_heure     TEXT NOT NULL,
          fichier        TEXT NOT NULL,
          pack_id        TEXT,
          resultat       TEXT NOT NULL,              -- importe | mis_a_jour | refuse
          messages_json  TEXT
        );

        CREATE INDEX ix_etat_echeance  ON etat_carte(echeance);
        CREATE INDEX ix_carte_pack     ON carte(pack_id);
        CREATE INDEX ix_section_pack   ON section(pack_id);
        CREATE INDEX ix_revision_jour  ON revision(jour);
        CREATE INDEX ix_revision_carte ON revision(carte_id);
        """,

        // ---- 2 : acquis (progression faite hors de l'application) ----------------------------
        """
        CREATE TABLE acquis (
          chemin        TEXT PRIMARY KEY,            -- clés des niveaux séparées par U+001F ; « § » + id du pack pour une séance
          avancement    TEXT,                        -- pas_vu | vu | revise ; NULL = hérité
          comprehension TEXT,                        -- decouverte | fragile | correct | solide ; NULL = hérité
          date_maj      TEXT NOT NULL
        );
        ALTER TABLE etat_carte ADD COLUMN prioritaire INTEGER NOT NULL DEFAULT 0;  -- nouvelle carte à montrer en premier
        """,

        // ---- 3 : planning du CNED (repère facultatif) ----------------------------------------
        """
        CREATE TABLE planning (
          id           TEXT PRIMARY KEY,             -- « id » du fichier de planning
          contenu_json TEXT NOT NULL,                -- le fichier tel quel (relu et validé à chaque chargement)
          source       TEXT,                         -- chemin du fichier importé
          date_import  TEXT NOT NULL
        );
        CREATE TABLE planning_surcharge (            -- dates choisies par l'étudiant ; le fichier n'est jamais modifié
          planning_id  TEXT NOT NULL,
          element      TEXT NOT NULL,                -- clé de la période ou du devoir (voir Core/Planning)
          debut        TEXT NOT NULL,                -- début de la période, ou semaine du devoir (AAAA-MM-JJ)
          fin          TEXT,                         -- fin de la période (NULL pour un devoir)
          PRIMARY KEY (planning_id, element)
        );
        """,

        // ---- 4 : taille et date du fichier de pack (le scan évite de relire un fichier inchangé) ----
        """
        ALTER TABLE pack ADD COLUMN fichier_taille INTEGER;
        ALTER TABLE pack ADD COLUMN fichier_modifie TEXT;
        """,

        // ---- 5 : signalements d'erreurs dans les cartes et les sections -----------------------
        """
        CREATE TABLE signalement (
          id              TEXT PRIMARY KEY,          -- GUID
          date_heure      TEXT NOT NULL,
          cible           TEXT NOT NULL CHECK (cible IN ('carte','section')),
          cible_id        TEXT NOT NULL,             -- id de la carte ou de la section dans le pack
          pack_id         TEXT NOT NULL,
          motif           TEXT NOT NULL,             -- reponse_fausse | question_pas_claire | faute | affichage | autre
          commentaire     TEXT,
          titre           TEXT,                      -- titre de la section
          texte_recto     TEXT NOT NULL,             -- texte affiché : recto de la carte, ou contenu de la section
          texte_verso     TEXT,
          pack_empreinte  TEXT,                      -- SHA-256 du pack au moment du signalement
          pack_date_maj   TEXT,
          matiere         TEXT,
          seance          TEXT,
          source_fichier  TEXT,
          page_source     INTEGER,
          etat            TEXT NOT NULL DEFAULT 'nouveau' CHECK (etat IN ('nouveau','exporte'))
        );
        """,

        // ---- 6 : masquer des packs (cours libres désactivés dans les Réglages) ---------------
        """
        ALTER TABLE pack ADD COLUMN masque INTEGER NOT NULL DEFAULT 0;
        """,

        // ---- 7 : collections de packs (cours libres, mes packs, contenus importés, imports manuels) ----
        """
        CREATE TABLE collection (
          cle            TEXT PRIMARY KEY,           -- libres | mes-packs | manuel | importe:<nom>
          nom            TEXT NOT NULL,
          masque         INTEGER NOT NULL DEFAULT 0, -- 1 = masquée (cartes et sections mises à l'écart, progression conservée)
          masque_depuis  TEXT                        -- jour du masquage : sert à étaler les échéances au réaffichage
        );
        ALTER TABLE pack ADD COLUMN collection TEXT;
        UPDATE pack SET collection = CASE WHEN id LIKE 'libre-%' THEN 'libres' ELSE 'mes-packs' END;
        INSERT INTO collection (cle, nom, masque)
          SELECT 'libres', 'Cours libres', MAX(masque) FROM pack WHERE collection = 'libres' GROUP BY collection;
        INSERT INTO collection (cle, nom, masque)
          SELECT 'mes-packs', 'Mes packs', 0 FROM pack WHERE collection = 'mes-packs' GROUP BY collection;
        """,
    ];
}

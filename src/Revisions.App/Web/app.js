// Page de rendu : reçoit du texte Markdown + LaTeX de l'application et l'affiche.
// Dialogue avec le C# par messages (window.chrome.webview).
(function () {
  "use strict";

  // html:false → le HTML brut contenu dans un pack est affiché comme du texte, jamais exécuté.
  const md = window.markdownit({
    html: false,
    linkify: false,
    highlight: function (code, langage) {
      if (langage && hljs.getLanguage(langage)) {
        try { return hljs.highlight(code, { language: langage }).value; } catch (e) { /* on retombe sur le texte brut */ }
      }
      return ""; // markdown-it échappe alors le code lui-même
    },
  });
  // texmath repère $…$ et $$…$$ avant que markdown-it ne touche aux _ et \\ des formules.
  md.use(window.texmath, { engine: window.katex, delimiters: "dollars", katexOptions: { throwOnError: false } });

  const contenu = document.getElementById("contenu");
  const hote = window.chrome && window.chrome.webview;
  const envoyer = (message) => { if (hote) hote.postMessage(message); };

  /** Nombre d'éléments affichés juste avant : ceux qui arrivent ensuite sont « révélés » (animation). */
  let precedent = 0;

  /** Position verticale d'un bloc dans la page, sans tenir compte des transformations (animations). */
  function positionDansLaPage(element) {
    let haut = 0;
    for (let e = element; e; e = e.offsetParent) haut += e.offsetTop;
    return haut;
  }

  /** Affiche la liste d'éléments envoyée par l'application. */
  function afficher(message) {
    const nouvelleCarte = message.defiler !== "bas";
    const revelesDepuis = precedent; // rang du premier bloc révélé par ce message
    contenu.replaceChildren();
    let rang = 0;
    for (const element of message.elements || []) {
      const bloc = document.createElement("div");
      bloc.className = element.genre + (element.etat ? " " + element.etat : "");
      // Révélation (réponse, étape, explication) : le nouveau bloc se retourne comme une carte (300 ms).
      if (!nouvelleCarte && rang >= precedent) bloc.classList.add("apparait");
      rang++;

      if (element.genre === "titre") {
        const titre = document.createElement("h1");
        titre.innerHTML = md.renderInline(element.markdown || "");
        bloc.appendChild(titre);
      } else if (element.genre === "etiquette") {
        bloc.textContent = element.markdown || "";
      } else if (element.genre === "comparaison") {
        // Deux colonnes : la réponse écrite par l'étudiant (texte brut) et la bonne réponse.
        const gauche = document.createElement("div");
        gauche.className = "colonne";
        const titreGauche = document.createElement("div");
        titreGauche.className = "etiquette";
        titreGauche.textContent = "Ta réponse";
        const saisie = document.createElement("div");
        saisie.className = "saisie";
        // Matrice remplie dans la grille : affichée comme la réponse (LaTeX construit par Atome, cases filtrées).
        // Sinon textContent : la saisie libre n'est jamais interprétée comme du HTML.
        if (element.saisieMarkdown) saisie.innerHTML = md.render(element.saisieMarkdown);
        else saisie.textContent = element.saisie || "";
        gauche.append(titreGauche, saisie);

        const droite = document.createElement("div");
        droite.className = "colonne";
        const titreDroite = document.createElement("div");
        titreDroite.className = "etiquette";
        titreDroite.textContent = "Réponse";
        const bonne = document.createElement("div");
        bonne.innerHTML = md.render(element.markdown || "");
        droite.append(titreDroite, bonne);
        bloc.append(gauche, droite);
      } else if (element.genre === "choix") {
        const lettre = document.createElement("span");
        lettre.className = "lettre";
        lettre.textContent = String.fromCharCode(65 + element.index);
        const texte = document.createElement("div");
        texte.innerHTML = md.render(element.markdown || "");
        bloc.append(lettre, texte);
        if (element.etat === "actif") {
          bloc.addEventListener("click", () => envoyer({ type: "choix", index: element.index }));
        }
      } else {
        if (element.genre === "etape") {
          const numero = document.createElement("div");
          numero.className = "numero";
          numero.textContent = "Étape " + element.index;
          bloc.appendChild(numero);
        }
        const texte = document.createElement("div");
        texte.innerHTML = md.render(element.markdown || "");
        bloc.appendChild(texte);
      }
      contenu.appendChild(bloc);
    }
    precedent = rang;
    // Nouvelle carte de session : elle arrive en se retournant ; nouvelle page de cours : un simple fondu.
    if (nouvelleCarte) {
      contenu.classList.remove("arrive-carte", "arrive-page");
      void contenu.offsetWidth; // relance l'animation
      contenu.classList.add(message.animation === "carte" ? "arrive-carte" : "arrive-page");
    }
    // Nouvelle carte ou nouvelle page : on repart du haut ; révélation : on montre la suite.
    if (message.defiler === "bas") {
      // Pas de scrollIntoView ici : au début de l'animation « revele », le bloc est replié (rotation),
      // sa hauteur visible est presque nulle et la page ne descendait pas. On calcule donc avec la
      // position de mise en page (offsetTop, sans transformation), une fois les polices KaTeX prêtes.
      const premierNouveau = contenu.children[Math.min(revelesDepuis, contenu.children.length - 1)];
      const dernier = contenu.lastElementChild;
      if (dernier) {
        const defiler = () => {
          const haut = positionDansLaPage(premierNouveau);
          const bas = positionDansLaPage(dernier) + dernier.offsetHeight + 16;
          // On montre tout le nouveau bloc ; s'il est plus haut que la fenêtre, on aligne son début.
          const cible = Math.min(haut - 16, bas - window.innerHeight);
          const doux = !document.documentElement.classList.contains("reduit");
          if (cible > window.scrollY) window.scrollTo({ top: cible, behavior: doux ? "smooth" : "auto" });
        };
        (document.fonts ? document.fonts.ready : Promise.resolve()).then(defiler);
      }
    } else {
      window.scrollTo(0, 0);
    }
  }

  /** Applique les couleurs du thème choisi dans l'application (variables CSS de app.css). */
  function appliquerTheme(variables) {
    for (const nom of Object.keys(variables || {})) {
      document.documentElement.style.setProperty(nom, variables[nom]);
    }
    // Animations réduites (réglage d'Atome ou de Windows) : de simples fondus très courts.
    document.documentElement.classList.toggle("reduit", (variables || {})["--animations"] === "0");
  }

  if (hote) hote.addEventListener("message", (e) => {
    if (e.data && e.data.type === "theme") appliquerTheme(e.data.variables);
    else afficher(e.data);
  });

  // Les touches utiles sont relayées à l'application (sinon les raccourcis ne marcheraient
  // plus dès qu'on clique dans le contenu).
  document.addEventListener("keydown", (e) => {
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    let touche = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    // Chiffres : on lit la position de la touche (e.code) et non le caractère, car sur un clavier
    // AZERTY la rangée du haut donne « & é " ' » sans Maj. Le pavé numérique marche aussi.
    const chiffre = /^(?:Digit|Numpad)([1-4])$/.exec(e.code);
    if (chiffre) touche = chiffre[1];
    if (/^[1-4a-es ]$/.test(touche) || ["Enter", "ArrowLeft", "ArrowRight", "Tab"].includes(touche)) {
      e.preventDefault();
      envoyer({ type: "touche", touche: touche });
    }
  });

  // Un lien ne doit jamais faire quitter la page de rendu.
  document.addEventListener("click", (e) => {
    const lien = e.target.closest && e.target.closest("a");
    if (lien) {
      e.preventDefault();
      envoyer({ type: "lien", href: lien.getAttribute("href") || "" });
    }
  });

  envoyer({ type: "pret" });
})();

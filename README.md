Auto-Activator L.I.S.A (.NET Version)
Contexte du Projet (TFE)
Ce projet s'inscrit dans le cadre d'un Travail de Fin d'Études (TFE) visant à automatiser les processus de test, de validation et d'activation de contrats d'assurance (ou contrats financiers) au sein d'un système d'information complexe (L.I.S.A / ELIA).

L'objectif principal est de fournir un outil robuste capable d'extraire des données contractuelles, de simuler des duplications/activations de contrats par injection de paiements, et de comparer techniquement l'état "Avant" et "Après" pour garantir la non-régression et l'intégrité des données lors des migrations ou des processus métiers.

Fonctionnalités Principales
L'application est une application Console interactive offrant trois modules majeurs :

Test d'Extraction : Extraction automatisée des différentes tables liées à un contrat cible (SCNTT0, SAVTT0, PRCTT0, etc.) et génération de fichiers Excel de diagnostic.

Module d'Activation (Simulation & Injection) :

Sauvegarde de l'état source d'un contrat via un système de "Snapshot" XML.

Simulation de duplication de contrat (ELIA).

Injection automatique d'un paiement en base de données pour forcer l'activation du nouveau contrat.

Module de Comparaison Intelligente :

Comparaison cellule par cellule entre le Snapshot du contrat source et les données live du nouveau contrat.

Application de règles d'exclusion métier pour ignorer les faux positifs (dates de modification, identifiants techniques générés).

Génération de rapports CSV détaillés et de KPI globaux (taux de succès par produit d'assurance).

Architecture et Structure du Code
L'architecture suit les principes de séparation des responsabilités (Separation of Concerns) pour garantir la maintenabilité et l'évolutivité du code :

Program.cs : Point d'entrée de l'application. Orchestre les flux de travail (Workflow) et gère l'interface utilisateur en ligne de commande (CLI).

/Config/Settings.cs : Centralise la configuration de l'application (chemins d'accès, chaînes de connexion SQL Server et Oracle). Utilise les variables d'environnement pour sécuriser les mots de passe de production.

/Config/Exclusions.cs : Dictionnaire métier qui répertorie les colonnes à ignorer globalement ou spécifiquement par table lors des comparaisons pour éviter les faux positifs.

/Services/DatabaseManager.cs : Couche d'accès aux données (DAL). Gère l'ouverture des connexions, l'exécution des requêtes SELECT et l'injection sécurisée (INSERT) paramétrée pour contrer les injections SQL.

/Services/Comparator.cs : Moteur de comparaison algorithmique. Normalise les données (arrondis des floats, gestion des nulls) et aligne les schémas avant de produire un rapport différentiel précis.

/Sql/Queries.cs : Dictionnaire centralisé ReadOnlyDictionary contenant toutes les requêtes SQL, facilitant leur modification sans toucher à la logique métier.

Choix Technologiques (Justifications TFE)
1. Langage et Framework : C# et .NET
   Performance et Typage fort : Contrairement à des scripts de test classiques (comme Python/Pandas), le choix de C#/.NET garantit des performances élevées, une gestion fine de la mémoire et une détection des erreurs à la compilation, sécurisant ainsi l'outil pour un usage industriel.

Intégration Entreprise : L'écosystème Microsoft est souvent la norme dans le monde de l'assurance/banque, facilitant l'intégration avec SQL Server (via Windows Authentication).

2. Accès aux données : ADO.NET (Microsoft.Data.SqlClient)
   Contrôle Absolu et Légèreté : Plutôt que d'utiliser un ORM lourd comme Entity Framework pour un schéma de base de données Legacy potentiellement tentaculaire, l'utilisation d'ADO.NET avec DataTable permet d'extraire dynamiquement n'importe quelle table sans avoir à créer des modèles de classes stricts pour chaque table.

3. Manipulation Excel : ClosedXML
   Simplicité et Rapidité : ClosedXML est une surcouche de DocumentFormat.OpenXml qui permet de générer des fichiers Excel natifs (.xlsx) sans nécessiter l'installation de Microsoft Office sur la machine hôte. Il permet d'insérer un DataTable complet en une seule ligne de code (worksheet.Cell(1, 1).InsertTable(dfTable)).

4. Approche de sauvegarde : Snapshots XML
   Résilience des tests : Pour comparer "Avant" et "Après", la sauvegarde de l'état source a été implémentée via DataTable.WriteXml(). C'est une méthode native, sérialisable et lisible par l'humain, préférée à la sérialisation binaire (comme Pickle en Python) car elle garantit la pérennité de l'historique des tests.

5. Algorithmique : HashSet et LINQ
   Optimisation de la complexité : Dans la classe Exclusions.cs, les listes de colonnes à exclure sont converties en HashSet<string>. Cela réduit la complexité de recherche des exclusions de O(N) à O(1), ce qui est crucial lors de la comparaison de milliers de cellules.

Génération de KPIs : L'utilisation de LINQ (Language Integrated Query) permet de grouper, compter et générer des statistiques métier complexes de manière très lisible et performante dans Program.cs.

Prérequis et Déploiement
Environnement : SDK .NET (version 6.0 ou supérieure recommandée).

Variables d'environnement :

DB_PWD : Mot de passe pour la base de données SQL Server si l'authentification Windows n'est pas utilisée.

Restitution des paquets NuGet :

Bash
dotnet restore
Paquets nécessaires : ClosedXML, Microsoft.Data.SqlClient.

Lancement :

Bash
dotnet run 
Valeur ajoutée du projet
Ce développement remplace des procédures manuelles longues et propices aux erreurs par un pipeline automatisé, répétable et auditable. Les rapports générés (détails techniques CSV et synthèses par produit) offrent à la fois une vue technique pour le débogage et une vue décisionnelle (KPI) pour le management.
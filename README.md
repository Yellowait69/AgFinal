E.T.L COMPARISON TOOL
A critical automation, data extraction, and environment comparison tool (D VS Q) for the LISA and ELIA systems.

📋 Table of Contents
Business Context and Objectives

Architecture and Tech Stack

Core Features (Deep-Dive)

3.1. Massive Asynchronous Extraction (LISA & ELIA)

3.2. Activation and Payment Injection

3.3. Intelligent Environment Comparison

3.4. Mainframe API Integration (MicroFocus)

Project Structure

Configuration and Customization

Usage (CLI)

Security and Implemented Best Practices

1. Business Context and Objectives
   The E.T.L COMPARISON TOOL was designed to address the complexity of integration and non-regression testing within  AG Insurance. A single contract's data is often scattered across multiple legacy (LISA) and modern (ELIA) systems, requiring the querying of dozens of SQL tables (over 30 targeted tables) and communication with Mainframe servers (MicroFocus).

The challenges solved by this tool:

Manual Overhead: Testing a contract activation manually used to take a considerable amount of time.

Test Reliability: Ensuring that a code modification on the Development environment (D000) does not alter standard behavior when compared to the Test environment (Q000).

Performance: Querying a massive volume of data concurrently without overloading the underlying transactional databases.

2. Architecture and Tech Stack
   Language: C# / .NET (Modern version fully supporting async/await asynchronous programming).

Database: SQL Server (via ADO.NET using System.Data.SqlClient).

HTTP Requests: Singleton HttpClient to communicate with the MicroFocus REST API.

JSON Parsing: Newtonsoft.Json (for parsing complex responses returned by the Mainframe).

Concurrency: Heavy use of multithreading (Task.WhenAll, SemaphoreSlim, ConcurrentDictionary).

💡 Why this architectural choice?
C# provides granular control over multithreading. To extract dozens of tables rapidly, breaking away from sequential execution was mandatory. However, bombarding a SQL Server with simultaneous queries causes connection timeouts and deadlocks. The asynchronous architecture, regulated by semaphores, offers the perfect compromise between extreme speed and server stability.

3. Core Features (Deep-Dive)
   3.1. Massive Asynchronous Extraction (LISA & ELIA)
   The ExtractionService module captures the complete snapshot of a contract (or a "Demand ID") at a specific point in time.

How it works: The tool resolves the identifier, then queries the LISA systems (table SCNTT0, endorsements, guarantees) and ELIA systems (TB5UCON, premiums, beneficiaries) in parallel.

Key Optimization (SemaphoreSlim(10)): The extraction strictly limits the number of concurrent database queries to 30.

Output Format: Generates timestamped and structured CSV files (e.g., Extraction_D_Uniq_182-2728195-31_20231024_1530.csv).

💡 The Technical Argument:
Without the SemaphoreSlim, executing 30+ simultaneous queries would lead to "Connection Pool Exhaustion". This throttle ensures the tool remains "Thread-Safe" and respects enterprise database resources. The output order of the tables is also guaranteed through the use of a synchronized list.

3.2. Activation and Payment Injection
The DatabaseManager doesn't just read data; it can write mock data to force a business process forward.

Payment Injection (InjectPaymentAsync): Generates a dummy payment with a valid structured communication (e.g., 820...99) and inserts it directly into the LV.PRCTT0 financial table.

Dynamic Parameters: The reference date is recalculated to simulate banking delays.

💡 The Technical Argument:
Rather than relying on slow GUIs or external payment gateways for testing, this tool short-circuits the process by injecting raw data exactly where the overnight batch expects it, drastically accelerating the testing cycle.

3.3. Intelligent Environment Comparison
Managed via the ComparisonOrchestrator and its associated models (ComparisonReport).

Goal: Compare a Base extraction file (e.g., yesterday's snapshot) with a Target extraction (today, after a code change).

Anti-Noise Filter (Exclusions.cs): Automatically ignores randomly generated technical identifiers (NO_CNT), modification timestamps (TSTAMP_DMOD, DB2 dates), and personal data (names, addresses).

💡 The Technical Argument:
A simple text diff tool would show a 100% error rate between two databases because of creation dates or primary keys. The Exclusions.cs class makes the comparator "intelligent" (business-focused), avoiding false positives. Using a HashSet<string> for these exclusions guarantees instantaneous O(1) lookups, even when comparing millions of data cells.

3.4. Mainframe API Integration (MicroFocus)
The MicroFocusApiService module is a robust HTTP client built to interact with the insurance company's batch server.

Custom Load-Balancing: The service knows a cluster of servers for each environment (e.g., sqmfas06, sqmfas08) and automatically fails over to another node if the first one is offline.

Job Submission (JCL): Submits a batch job, parses the returned job number (e.g., J12345), polls its status, and extracts the execution log (Spool / DDView), specifically targeting BERPCTLO or SYSOUT.

💡 The Technical Argument:
Utilizing a single static HttpClient prevents local port exhaustion (Socket Exhaustion). Managing session cookies (esadmin-cookie) avoids the overhead of re-authenticating on every request, speeding up the overall execution.

4. Project Structure 
🔹 Root Directory
   Program.cs: This is the historical entry point of the application (console mode). It provides a text-based menu to launch extractions, activations, or comparisons via the command line.

MainWindow.xaml / MainWindow.xaml.cs: The main window of the graphical user interface (WPF). It serves as a navigation container to display the application's different views (Extraction, Activation, Comparison, Help).

LoginWindow.xaml / LoginWindow.xaml.cs: The login window. It allows the user to authenticate (likely against the MicroFocus Mainframe or SQL Server) before accessing the core features.

🔹 Config Directory
Config/Settings.cs: The central configuration file. It manages the creation of file paths (input, output, and snapshot folders) and contains the logic to dynamically generate the database connection string (SQL Server) based on the chosen environment (e.g., D000, Q000).

Config/Exclusions.cs: Contains the business rules for the comparison engine. It lists all the columns that must be ignored during a comparison (such as randomized technical identifiers, creation/modification dates, and personal data) to prevent "false positives".

🔹 Models Directory
Models/ExtractionModels.cs: Defines the data structures (Data Transfer Objects - DTOs). It contains classes like ExtractionResult (to store extracted content), BatchProgressInfo (to track progress), and ComparisonReport (to structure comparison results).

🔹 Services Directory (The business core of the application)
Services/DatabaseManager.cs: The SQL Server database management service. It is used to test connections, execute asynchronous queries securely (anti-SQL injection), and inject simulated payments.

Services/ExtractionService.cs: The extraction orchestrator. It queries dozens of tables (LISA and ELIA) in parallel, utilizing a throttling system (SemaphoreSlim) to avoid overloading the databases.

Services/MicroFocusApiService.cs: The REST API client communicating with the Mainframe. It features a built-in Load-Balancing system to switch servers if one goes offline, submits JCL batch jobs, and retrieves execution logs.

Services/Comparator.cs: The line-by-line comparison engine.

Services/ComparisonOrchestrator.cs: Oversees the logic of the Comparator. It orchestrates the opening of Base and Target files, applies exclusions, and generates the final comparison report.

Services/BatchExtractionService.cs: Allows for massive extractions (batch processing) instead of processing a single contract at a time.

Services/BatchActivationService.cs & Services/ActivationOrchestrator.cs: Services dedicated to the massive contract activation logic, simulating payments or triggering business processes on multiple queued items.

Services/ActivationDataService.cs: Service for reading or preparing activation data (e.g., from an input Excel or CSV file).

Services/JclProcessorService.cs: A specialized module for handling, generating, or processing JCL (Job Control Language) scripts prior to sending them to the Mainframe via the API.

🔹 Sql Directory
Sql/Queries.cs: A centralized dictionary containing all the application's plain-text T-SQL queries. Every query uses the WITH(NOLOCK) command to prevent data locking in the database.

🔹 Utils Directory
Utils/CsvFormatter.cs: A utility class (Helper). It is responsible for intelligently and efficiently converting DataTable objects (the SQL query results) into a CSV text format compatible with the output files.

🔹 Views Directory (Graphical User Interface - WPF)
Each UI view consists of a design file (.xaml) and a code-behind logic file (.xaml.cs).

Views/ActivationView.xaml / Views/ActivationView.xaml.cs: The UI screen used to manage the activation of one or multiple contracts.

Views/ComparisonView.xaml / Views/ComparisonView.xaml.cs: The screen that allows users to select a "Base" environment file and a "Target" environment file to visually display their comparison results.

Views/ExtractionView.xaml / Views/ExtractionView.xaml.cs: The screen for entering a contract number and launching a LISA/ELIA extraction, complete with a progress bar display.

Views/HelpView.xaml / Views/HelpView.xaml.cs: A technical assistance or integrated documentation screen explaining how to use the tool to the user.
5. Configuration and Customization
   All configuration is centralized within Config/Settings.cs. The tool dynamically detects the target environment and generates the appropriate connection string:

Auto Server Detection: If you input the D000 environment, the tool will route traffic to SQLMFDBD01 and the FJ0AGDB_D000 database. If you input Q000, it points to SQLMFDBQ01.

Authentication: The tool uses Integrated Security=True (Windows Authentication) by default but can easily switch to standard SQL Server authentication via the in-memory Uid and Pwd properties.

6. Usage (CLI)
   Upon launching the application (via Visual Studio or the compiled executable), an interactive menu is displayed:

Plaintext
==================================================
              E.T.L COMPARISON TOOL      
==================================================
1. Run Single Extraction (LISA & ELIA)
2. Run Activation
3. Run Comparison
0. Exit
   ==================================================
   Option 1: Ideal for diagnosing a specific contract. Exports the entirety of the LISA/ELIA data into a flat, Excel-friendly file. It prompts for the contract number (e.g., 182-2728195-31) and the environment (D000). The tool natively handles "Demand IDs" if needed.

Option 2: Prepares the groundwork by validating the contract's presence in the target DB, which is a prerequisite before payment injection or batch execution.

Option 3: The core Quality Assurance (QA) tool. It prompts for two file paths generated by Option 1 and prints the exact percentage of business similarity to the console.

7. Security and Implemented Best Practices
   This code was designed for a critical production environment:

Anti-SQL Injection (SqlParameter): Absolutely all variables injected into the database (Contract Numbers, Dates, Amounts) are passed through command.Parameters.AddWithValue(...). No string concatenation is ever used for input variables.

No-Locking (WITH(NOLOCK)): All SELECT queries in Queries.cs use the WITH(NOLOCK) directive. This guarantees that our massive testing/extractions will never place blocking locks on the enterprise tables, preventing environment crashes.

Network Resilience: In MicroFocusApiService, if an API server goes down, the foreach loop catches the error and moves to the next server without crashing the application. A final error report is only thrown if all nodes are unreachable.

Memory Optimization: The use of StringBuilder during the extraction process prevents memory over-allocation (Garbage Collection overhead) when creating massive CSV files.
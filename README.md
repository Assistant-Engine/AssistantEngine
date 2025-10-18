
# Assistant Engine

Assistant Engine is a developer-focused AI tool and UI.
It enables running and interacting with multiple AI models locally, with deep understanding of code, advanced code comprehension, RAG (Retrieval-Augmented Generation), Text-to-SQL, and PowerShell execution, all without relying on external cloud APIs.

> Unlike most AI tools focused mainly on Python, Assistant Engine pushes AI-assisted development into the C# and .NET ecosystem with leading code intelligence.

![Assistant Engine Screenshot](/demo.png)

> [!TIP]  
>  **Need an Enterprise License?** – **[Contact our team](mailto:support@assistantengine.ai)**  
>
> Enjoy **enterprise-only benefits**, including **custom branding and UI options**, **priority SLA-backed support**, **long-term maintenance (LTS) releases**, plus **team management tools**, **training resources**, and **the ability to request new features** tailored to your needs.

For up to date and detailed information, be sure to check out [Assistant Engine Documentation](https://docs.assistantengine.ai/)..

---

## Key Features

* **Multi-Model AI Support** - Run and interact with multiple local models via [Ollama](https://ollama.ai/).
* **Deep C# Comprehension** - Powered by Semantic Kernel and custom chunking strategies.
* **RAG with Vector Stores** - Retrieve from code, text, and database schema.
* **Text-to-SQL** - Convert natural language into SQL queries.
* **Local PowerShell Execution** - AI-assisted automation and scripting.
* **Database Integration** - MSSQL, SQL Server, and Postgres supported.
* **Privacy First** - All features run locally, offline, and secure.

---

## Quick Start (Binary Release)

* [Download Assistant Engine](https://assistantengine.ai/downloads) - Windows and macOS builds
* [Install Ollama](https://ollama.ai/download) - Only required if you want to run models locally.  

  *(By default, Assistant Engine comes with preconfigured remote assistants so you can try it immediately.)*

---
## Local Development Guide

Assistant Engine is built to run **natively across Web, Windows, macOS, iOS, and Android** - all from a single .NET 9 codebase. Whether you're interested in extending its AI features, improving the UI, or building new developer tools, your contributions are welcome.

---

### Prerequisites

Before you begin, make sure your development environment is ready:

* **Visual Studio (recommended)**
* **.NET 9 SDK** installed
* In the **Visual Studio Installer**, ensure the following workloads are enabled:

    * **ASP.NET and web development**
    * **.NET Multi-platform App UI (MAUI)**
* **Ollama**:

    * Connect to an existing Ollama server, or [install Ollama locally](https://ollama.ai/)
* **AI Templates**:
    Install Microsofts AI project templates with:

    ```bash
    dotnet new install Microsoft.Extensions.AI.Templates
    ```

> [!NOTE]  
> Common setup issue: Make sure `.NET Multi-platform App UI development` is checked in the Visual Studio installer.

---

### Setting Up Your Local Environment

1. **Clone the Repository**
   Open a terminal and run:

   ```bash
   git clone https://github.com/Assistant-Engine/AssistantEngine.git
   ```

2. **Open in Visual Studio**
   Open `Assistant Engine.sln`. You'll see three main projects:

   | Project        | Description                                                                               |
   | -------------- | ----------------------------------------------------------------------------------------- |
   | **AssistantEngine.Web** | Blazor-based web application interface.                                                   |
   | **AssistantEngine.UI**  | Shared Razor Class Library (RCL) with core logic and UI components.                       |
   | **AssistantEngine.App** | .NET MAUI app (Windows, macOS, iOS, Android) with WebView2 hosting the Assistant Engine experience. |

   Run this once at the project root to restore workloads:

   ```bash
   dotnet workload restore
   ```

3. **Run the Application**

   * To run the full cross-platform client: **start `AssistantEngine.App`**
   * To run only the web interface: **start `AssistantEngine.Web`**

And that's it, you're ready to start building with Assistant Engine!


# Distributed File Repository System (DFS) 📂🌐

A high-performance, multi-threaded C# application designed for centralized file management in a networked environment. This project demonstrates advanced socket programming, implementing a hybrid **UDP/TCP** architecture to ensure both efficient client discovery and reliable data transmission.

## 🛠️ System Architecture

The system is partitioned into three core modules to ensure high cohesion and low coupling:

* **Core Library (Biblioteka):** Shared assembly containing common data structures, request models, and utility classes used by both endpoints.
* **RepoServer:** The backbone of the system. It handles asynchronous UDP broadcasts for client discovery and manages dedicated TCP streams for file operations.
* **RepoClient:** A lightweight client application providing an intuitive interface for interacting with the remote repository.

## ✨ Key Features

### 🔌 Hybrid Networking
* **UDP Handshaking:** Implements a fast UDP protocol for initial client-to-server discovery and login.
* **Reliable TCP Streams:** All file transfers and command executions (Read, Write, Delete) are handled via TCP sockets to guarantee data integrity.

### 🔐 Data Integrity & Concurrency
* **Exclusive File Locking:** Prevents data corruption by automatically locking files during modification or creation, ensuring thread-safe operations in a multi-client environment.
* **Priority Request Queue:** Implements a server-side queuing system to handle high volumes of concurrent client requests efficiently.

### 📊 Management & Analytics
* **Full CRUD Operations:** Remote capability to read, create, modify, and permanently delete files within the server’s repository.
* **Client Statistics:** The server tracks and generates performance metrics per client, monitoring activity logs and operation frequencies.

## 💻 Technologies & Requirements
* **Language:** C#
* **Framework:** .NET Framework 4.8+
* **Networking:** System.Net.Sockets (TCP/UDP)
* **Concurrency:** Multi-threading & Task Parallel Library (TPL)

## 🚀 How It Works
1.  **Server Startup:** The `RepoServer` initializes and begins listening for UDP login packets on a designated port.
2.  **Client Discovery:** The `RepoClient` sends a broadcast/direct UDP signal to authenticate.
3.  **Session Establishment:** Once authenticated, a persistent TCP connection is established for secure command execution.
4.  **File Operation:** Clients request access to files; if a file is being edited, the server places the request in a queue or applies a lock.

---
*Developed as part of the "Application of Computer Networks in Infrastructure Systems" curriculum.*

# ⚔️ CRYPT ARENA: Mythic Hybrid Battleground

**CRYPT ARENA** is a terminal-based (CLI), real-time multiplayer battle arena game developed using **C#** and **.NET Socket libraries**.  
The project implements a **Hybrid Networking Architecture**, combining **TCP** and **UDP** protocols to deliver both **data integrity** and **high-performance gameplay** simultaneously.

- **Course:** Network Programming  
- **Status:** Final Release (Completed)

---

## 🚀 Technical Architecture & Implementation Details

This project is built upon a **Hybrid Network Model** to fully satisfy and exceed the course requirements.

---

## 🌐 1. Hybrid Networking Model

### 🔒 TCP (Transmission Control Protocol)
**Usage:**  
Used for critical data that requires guaranteed delivery and order.

**Implementation:**  
- `TcpListener`
- `TcpClient`

**Data Handling:**  
- `StreamReader`
- `StreamWriter`  
(Secure, text-based data stream processing)

**Key Functions:**  
- Lobby authentication  
- "Ready" state synchronization (`CMD:READY`)  
- Health (HP) updates  
- Win/Lose condition broadcasts  

---

### ⚡ UDP (User Datagram Protocol)
**Usage:**  
Used for high-frequency, real-time data where speed is prioritized over reliability.

**Implementation:**  
- `UdpClient`
- Broadcasting techniques

**Key Functions:**  
- Low-latency Player Movement (X/Y coordinates)  
- Projectile & Combat tracking  

---

## 🔒 2. Thread Safety & Concurrency

### 🧵 Multi-threading
The server runs dedicated threads for:
- Client acceptance & management  
- Main **Game Loop** (tick rate)  
- **Item Spawner** system  

### 🔐 Concurrency Control
To prevent **race conditions** and server crashes, all access to shared resources (such as `Dictionary<_players>` and `List<_items>`) is protected using the `lock` mechanism.

This guarantees **absolute thread safety** during simultaneous read/write operations.

---

## 🎮 Gameplay Features

### ⚔️ Faction & Class System

#### Two Opposing Teams
- 🔵 **Alliance of Light** — Spawns on the left side of the arena  
- 🔴 **Legion of Shadow** — Spawns on the right side of the arena  

#### Character Classes

| Class        | Icon | Role / Stats        | Projectile |
|--------------|------|---------------------|------------|
| Necromancer  | 🧙   | High Mana, DPS      | 💀         |
| Paladin      | 🛡️   | High HP (Tank)      | ✨         |
| Rogue        | 🥷   | Balanced, Agile     | 🗡️         |
| Vampire      | 🧛   | Balanced            | 🩸         |

---

### 🍷 Dynamic Item System (Item Spawner)

- **Server-Side Authority:**  
  The server runs an autonomous thread that spawns a **Health Potion (🍷)** every **20 seconds** at a random, valid (non-wall) coordinate.

- **Collection Logic:**  
  When a player moves over a potion, they gain **+20 HP**.

- **Synchronization:**  
  Upon collection, the server broadcasts an `ITEM:DESTROY` packet to all clients to remove the item visually, ensuring full game state consistency.

---

## ⏱️ Game Loop Mechanics

### 🧩 Lobby Phase
- Players connect via **TCP**
- Select class and team
- Toggle **READY** state using the `R` key

### ⚔️ Battle Phase
- Host starts the match
- A **60-second timer** begins
- Movement and combat are enabled

### 🏆 Victory Conditions
- **Sudden Death:**  
  If a team is wiped out, the game ends immediately.
- **Time Limit:**  
  When the timer reaches zero, the team with the **highest total HP** wins.

### 🔄 Auto-Reset
- The server automatically resets:
  - HP
  - Mana
  - Player positions  
- Reset occurs **5 seconds after match end**
- Players are returned to the lobby

---

## 👻 Spectator (Ghost) Mode

- Eliminated players are **not disconnected**
- They transform into **Ghosts (👻)**
- Ghosts can freely roam the arena to spectate
- No interaction with living players or items

---

## 🖥️ Visual & Control Enhancements

### ✨ Anti-Flicker Engine
Instead of clearing the entire console using `Console.Clear()`, the client uses a **smart rendering algorithm** that only updates changed pixels, resulting in smooth visuals.

### 🎯 Speed Normalization
Due to the aspect ratio of terminal characters:
- Horizontal movement: **2 units**
- Vertical movement: **1 unit**

This creates visually balanced movement speed.

---
## 🕹️ How to Start the Game

### 1️⃣ Server Initialization
Wait for the **"SERVER ONLINE"** message.

---

### 2️⃣ Start the Host Client
Run the following file:

**GameClient.exe**

Then select:  
**[1] HOST**

---

### 3️⃣ Connect Other Players
Run the following file on other machines:

**GameClient.exe**

Then select:  
**[2] JOIN**

Enter the Host's IP Address  
*(example: `192.168.1.XX`)*

> ℹ️ **Note:** If you are running the game on the same computer as the host, you can skip entering the IP address by simply pressing **ENTER**.

---

### 4️⃣ Play
- All players press **R** to toggle **READY**
- Host presses **ENTER** to start the match

---

## ⚔️ Welcome to the Crypt Arena

**Choose your side.**  
**Dominate the battleground.**

# ⚔️ CRYPT ARENA: Mythic Hybrid Battleground

**CRYPT ARENA** is a terminal-based (CLI) multiplayer arena game developed in **C#**. The game uses a **Hybrid Networking Architecture**, combining **TCP** and **UDP** protocols to deliver a synchronized, high-performance, low-latency combat experience directly in the terminal.

---

## 🚀 Current Features (v1.0)

### 🌐 Hybrid Networking Model

* **TCP (Transmission Control Protocol)**

  * Used for critical and reliable data:

    * Player login & authentication
    * Health (HP) synchronization
    * Death events
  * Guarantees **100% delivery and order**

* **UDP (User Datagram Protocol)**

  * Used for high-frequency, real-time data:

    * Player movement
    * Projectile positions
  * Ensures **minimal latency** for responsive gameplay

---

### 🧙 Character Class System

The game features **4 unique playable classes**, each with distinct stats and visual identity:

| Class       | Theme           | Projectile |
| ----------- | --------------- | ---------- |
| Necromancer | Dark Magic      | 💀         |
| Paladin     | Holy Power      | ✨          |
| Rogue       | Stealth & Speed | 🗡️        |
| Vampire     | Blood Magic     | 🩸         |

Each class has customized:

* HP
* Mana
* Damage values

---

### 🖥️ Advanced Rendering Engine (Anti-Flicker)

* Uses `_drawLock` and `_lastDrawnPositions` mechanisms
* Prevents terminal flickering by:

  * Avoiding full screen clears
  * Updating **only changed coordinates** (partial rendering)

---

### 👻 Spectator Mode

* Players **do not disconnect upon death**
* Instead, they enter **Ghost Mode (👻)**
* Ghost players can:

  * Freely roam the arena
  * Spectate ongoing battles in real-time

---

## 🛠️ Development Roadmap (Upcoming Updates)

Planned features to expand gameplay depth and fulfill advanced project requirements:

### 1️⃣ Mythic Alliances & Friendly Fire (Team System)

* [ ] **Team Selection**: Choose between

  * Covenant of Light
  * Order of Shadow
* [ ] **Friendly Fire Logic**:

  * Projectiles pass through allies
  * No damage dealt to teammates

---

### 2️⃣ Time Limits & Round System (Game Rounds)

* [ ] **Global Timer**: Server-side 60-second countdown per round
* [ ] **Victory Conditions**:

  * Total team HP calculated at round end
  * Team with highest remaining HP wins
* [ ] **Auto-Reset System**:

  * Arena cleanup
  * Automatic player respawn for next round

---

### 3️⃣ Mythic Map & Cover System

* [ ] **Static Obstacles**:

  * Crystal Pillars (◈)
  * Ancient Gravestones (†)
* [ ] **Collision Logic**:

  * Projectiles are destroyed upon obstacle impact
  * Enables tactical positioning and cover usage

---

### 4️⃣ Power Crystals & Essence of Life (Power-Ups)

* [ ] **Health Orbs**:

  * Randomly spawning green essences
  * Restore player HP
* [ ] **Mana Crystals**:

  * Blue diamonds
  * Instantly refill mana

---

### 5️⃣ Combat Mechanics (Combo Attacks)

* [ ] **Critical Strike System**:

  * 3 consecutive hits on the same target within 2 seconds
  * Triggers massive bonus damage
* [ ] **Visual Feedback**:

  * "COMBO!" notifications displayed on screen

---

## 💻 Technical Architecture

### 🖧 Server

* Acts as the **central networking hub**
* Responsibilities:

  * Manages TCP client connections
  * Broadcasts critical events
  * Distributes UDP packets to all active sessions

### 🧑‍💻 Client

* Captures user input via `Console.KeyAvailable`
* Synchronizes local game state with server data

### 🔒 Concurrency & Thread Safety

* Multi-threaded architecture
* Uses `lock` blocks to ensure:

  * Thread safety
  * Data consistency during high-frequency updates

---

## ▶️ How to Run

1. Launch **`GameServer.exe`** first to initialize the network hub
2. Open multiple instances of **`GameClient.exe`** to join the arena

---

> ⚔️ Enter the Crypt. Choose your class. Dominate the arena.

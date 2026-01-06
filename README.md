# 🎮 Crypt Battle Arena  
*(Emoji Battle Royale + Cafeteria Food Fight Hybrid)*

## 📌 Project Overview

This project is a **text-based multiplayer real-time arena game** developed for the **Network Programming** course.  
It is a **hybrid implementation** inspired by:

- **Project 1: Emoji Battle Royale**
- **Project 6: Cafeteria Food Fight Simulator**

The game combines **real-time emoji-based combat** with **projectile mechanics, team battles, and power-ups**, demonstrating the effective use of **TCP and UDP socket programming** for different networking requirements.

---

## 🎯 Core Concept

- Players control emoji characters in a shared arena
- Two teams: **Light (💙)** and **Shadow (❤️)**
- Real-time movement and combat
- Projectile-based attacks
- Limited attack resources using a **Mana-based Ammo Limitation system**
- The team with the highest total HP at the end of the round wins

---

## 🔫 Ammo Limitation System (Mana-Based)

Instead of traditional ammunition, this project uses **Mana** as an **ammo limitation mechanic**:

- Each attack consumes **mana**
- Players cannot attack when mana is insufficient
- Mana regenerates automatically over time
- This enforces:
  - Resource management
  - Tactical decision-making
  - Balanced combat pacing

This design fulfills the **ammo limitation requirement** of *Project 6* while remaining suitable for a text-based real-time game.

---

## 🌐 Network Architecture

### 🧠 TCP – Reliable Communication

TCP is used for **critical and reliable operations** where packet loss is unacceptable.

**Used for:**
- Player status synchronization (HP updates)
- Lobby system (ready state, host control)
- Game start commands
- Reliable state updates

**Technologies:**
- `TcpListener`
- `TcpClient`
- `NetworkStream`
- Multithreaded client handling

---
### ⚡ UDP – Real-Time Communication

UDP is used for **fast-paced, real-time gameplay** where performance is prioritized over guaranteed delivery.

**Used for:**
- Player movement updates
- Projectile broadcasting
- Item spawn and destruction events
- Game timer synchronization
- Arena state updates

Because UDP is **connectionless**, the server does not receive an explicit
disconnect event when a client closes.

To address this, a **server-side heartbeat and timeout mechanism** was implemented:

- Each movement update refreshes the player's "last seen" timestamp
- Players inactive for more than **5 seconds** are considered disconnected
- Disconnected players are:
  - Removed from the server player registry
  - Removed from the lobby list
  - Logged on the server console
- If the disconnected player was the **host**, host authority is reassigned automatically

This ensures a **consistent and authoritative lobby state**.
**Technologies:**
- `UdpClient`
- UDP broadcasting
- Heartbeat and timeout mechanism for disconnect detection

---

## 🎮 Gameplay Features

### ✅ Implemented Core Features

- Emoji-based player characters
- Real-time multiplayer arena
- Lobby system with host control
- Team-based combat (2 teams)
- Projectile attack system
- Mana-based ammo limitation
- Health & mana HUD
- Power-up items (healing)
- Round timer
- Win/lose determination
- Automatic round reset

---

## 🧵 Multithreading

The project uses **multiple threads** to manage:

- TCP client connections
- UDP message listening
- Game timer loop
- Mana regeneration
- Item spawning

Thread safety is ensured using `lock` mechanisms where shared state is accessed.Additional synchronization mechanisms were added on the server side to ensure
thread-safe access to shared player and connection state.


---


## 🛠 Technologies & Requirements

- **Language:** C#
- **Framework:** .NET 8.0
- **Application Type:** Multi-threaded Console Application

**Networking APIs used:**
- `TcpListener`
- `TcpClient`
- `UdpClient`
- `NetworkStream`

---

## ▶️ How to Run

### Prerequisites
- Ensure **[.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)** is installed.   

### 1️⃣ Start the Server
```bash
dotnet run --project GameServer
```

### 2️⃣ Start Clients

**Option A: Using Terminal (Dev Mode)**
Open new terminal windows (one for each player) and run:

```bash
dotnet run --project GameClient
```
--- OR (Recommended for cleaner Gameplay) ---

**Option B: Using Executable (.exe)**

Navigate to the build folder: GameClient \ bin \ Debug \ net8.0

Double-click GameClient.exe to launch. (You can open multiple instances for multiplayer testing)


## ▶️ Client Steps
1. Enter username  
2. Select class  
3. Choose team  
4. Press **R** to ready up  

The **host** starts the game by pressing **ENTER**.

---

## 🧪 Testing Scenarios

- Minimum **3 simultaneous clients** tested
- Player disconnection handling via **server-side timeout mechanism**
- Automatic removal of disconnected players from lobby
- Host reassignment when the original host disconnects
- Server restart scenarios
- Real-time synchronization under **UDP packet loss**
- Invalid and late client handling

---

## ⚠️ Known Limitations

- TCP communication uses `NetworkStream` directly instead of `StreamReader` / `StreamWriter`  
- No persistent player data between sessions  
- Console rendering depends on terminal size  
- UDP disconnects are inferred via timeout rather than explicit events


These choices were made to prioritize **performance**, **simplicity**, and **clarity**.

---

## 🤖 AI Tool Usage

AI-assisted tools were used strictly as **learning, review, and debugging aids**,
in full compliance with the course AI usage policy.

No complete project, class, or system was generated automatically.
All design decisions and implementations were made by the developer.

### Usage Policy Compliance

All AI-generated suggestions were:
- Carefully reviewed and evaluated
- Manually implemented by the developer
- Tested within the application
- Fully understood before integration

AI tools were not used to generate full solutions or replace independent problem-solving.

### Tools Used

**ChatGPT**
- Clarifying TCP vs UDP design decisions  
- Reviewing multithreading and synchronization logic  
- Debugging network communication issues  

**Google Gemini**
- Exploring alternative architectural approaches  
- Validating client-server responsibility separation  

**Claude**
- Improving code readability suggestions  
- Reviewing documentation clarity and structure  

All AI-generated suggestions were:
- Carefully reviewed  
- Manually implemented  
- Tested within the application  
- Fully understood by the developer  

No complete project or class was generated automatically.

---

## 🏁 Conclusion

This project demonstrates:

- Practical usage of **TCP and UDP**
- Real-time multiplayer synchronization
- **Mana-based ammo limitation** mechanics
- Creative **hybrid game design**

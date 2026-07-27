<h1 align="center">
  <br>
  <img src="Frontend/src/assets/ISEL%20Logo.svg" alt="OEIMS" width="200">
  <br>
  OEIMS
  <br>
</h1>

<h4 align="center">A Lightweight Monitoring System for Online Exam Integrity.</h4>

<p align="center">
  <a href="../../graphs/contributors">
    <img src="https://img.shields.io/github/contributors/sancheslfl/oeims" alt="Contributors"/>
  </a>
  <a href="../../stargazers">
     <img src="https://img.shields.io/github/stars/sancheslfl/oeims" alt="Stars"/>
  </a>
  <a href="../../issues">
     <img src="https://img.shields.io/github/issues/sancheslfl/oeims" alt="Issues"/>
  </a>
  <a href="../../pulls">
     <img src="https://img.shields.io/github/issues-pr/sancheslfl/oeims" alt="Pull requests"/>
  </a>
  <a href="../../commits/main">
     <img src="https://img.shields.io/github/last-commit/sancheslfl/oeims" alt="Last commit"/>
  </a>
</p>

<p align="center">
  <a href="#documentation">Documentation</a> •
  <a href="#key-features">Key Features</a> •
  <a href="#credits">Credits</a> •
  <a href="#related">Related</a> •
  <a href="#developers">Developers</a>
</p>

OEIMS is an online exam integrity monitoring system developed in the context of the Project and Seminary course of the
BSc in Computer and Software Engineering at the Instituto Superior de Engenharia de Lisboa.

The system combines a web platform for professors and students, a central server, and a Windows Sentinel installed on
the student machine. Sentinel collects a limited set of operating system signals and sends structured events to the
professor dashboard in real time.

OEIMS focuses on **detection and reporting instead of lockdown**. Events such as focus loss, forbidden processes,
network changes, or a missing heartbeat support the professor's judgment, but do not automatically prove academic
misconduct.

The repository also includes a local bootstrap so the complete presentation environment can be built, installed, and
started from the source repository without preparing a separate release package.

---

## Documentation

### Deployment

The complete local presentation setup currently targets Windows. Before starting, install and run
[Docker Desktop](https://www.docker.com/products/docker-desktop/) and install the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

The server sends account and verification emails over SMTP and will not start without SMTP
credentials. Before the first run, copy `.env.example` to `.env`:

```powershell
Copy-Item .env.example .env
```

Then edit `.env` and set `SMTP_HOST`, `SMTP_USERNAME`, and `SMTP_PASSWORD` to the values given by an
SMTP provider of your choice. A sandbox email service intended for development is the simplest
option, since production mail providers usually require additional account setup. Leave `JWT_SECRET`,
`OEIMS_PORT`, and `FRONTEND_BASE_URL` as they are; the start script fills them in automatically.

After cloning the repository and preparing `.env`, open PowerShell as Administrator and run:

```powershell
.\Scripts\bootstrap.ps1
```

The bootstrap script starts the web platform, installs and starts Sentinel, and opens the professor console. The Agent
starts immediately, but clipboard blocking begins only after the student completes verification and receives the exam
identity code.

When the console opens, sign in with either professor account created automatically:

* `professor@isel.pt` / `profpass123`
* `professor2@isel.pt` / `profpass123`

To run only the server and frontend on the same machine:

```powershell
.\Scripts\start-oeims.ps1
```

To stop the web platform or remove Sentinel:

```powershell
.\Scripts\stop-oeims.ps1
.\Scripts\uninstall-sentinel.ps1
```

The uninstall script keeps Sentinel machine configuration and authorization data by default; pass `-RemoveData` to
delete it as well. Passing `-RemoveData` to `stop-oeims.ps1` is a separate action: it removes the server's database
volume, so the stored exams, sessions, and accounts are lost.

### Wiki

The project wiki contains technical and user-oriented documentation associated with the repository and is available
[here](../../wiki).

### Final Report

The final report is developed as part of the BSc in Computer and Software Engineering and will be added to this
repository when the final version is delivered.

### Additional Documentation

The main source folders contain the implementation of the
[server](Server), [frontend](Frontend), and [Sentinel](Sentinel). The local deployment scripts are available in the
[Scripts](Scripts) folder.

---

## Key Features

* **Distributed Architecture**: The system separates the central server, web platform, Windows Service, and desktop
  Agent according to their responsibilities;
* **Real-Time Supervision**: Monitor events and connection state are delivered to the professor console during the exam;
* **Operating-System Monitoring**: Sentinel observes focus changes, forbidden processes, network changes, clipboard
  restrictions, and service or Agent liveness;
* **Detection-Oriented Design**: The system reports integrity signals without automatically deciding that misconduct
  occurred;
* **Privacy-Conscious Events**: Sentinel sends structured metadata instead of screen recordings, file contents, or
  keystrokes;
* **Centralized Local Bootstrap**: The complete presentation environment can be built, installed, and started directly
  from the repository with a single bootstrap script, after a one-time SMTP configuration.

---

## Credits

This software uses the following open source packages and platforms:

* [Kotlin](https://kotlinlang.org/)
* [Ktor](https://ktor.io/)
* [Exposed](https://github.com/JetBrains/Exposed)
* [SQLite](https://www.sqlite.org/)
* [.NET](https://dotnet.microsoft.com/)
* [React](https://react.dev/)
* [Tailwind CSS](https://tailwindcss.com/)
* [Docker](https://www.docker.com/)

---

## Related

* [Safe Exam Browser](https://safeexambrowser.org/) - A lockdown-oriented browser for controlled exam environments;
* [Moodle Quiz](https://docs.moodle.org/en/Quiz_activity) - An online assessment activity provided by Moodle;
* [Open edX](https://openedx.org/) - An open-source platform for online learning and assessment.

---

## Developers

* [Miguel Sanches](https://github.com/sancheslfl)
* [ytaccc](https://github.com/ytaccc)

### Supervisors

* Project supervisors at the Instituto Superior de Engenharia de Lisboa.

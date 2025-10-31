# Lab3 - ASP.NET Core HTTP Request Processing, Caching, State Management

This project is an implementation of **Lab Work #3**, focused on:
✔️ HTTP request processing in ASP.NET Core  
✔️ Middleware creation using Run / Use / Map  
✔️ State management using Cookies & Session  
✔️ In‑memory caching with IMemoryCache  
✔️ Minimal API (no MVC)  
✔️ Dynamic HTML generation  
✔️ Pagination, search, data insertion  
✔️ GitHub Actions CI build workflows  

---

## ✅ Features Implemented

| Feature | Status | Details |
|--------|:-----:|---------|
| Minimal API app without MVC | ✅ | HTML directly via Response.WriteAsync |
| Caching 20 rows per table | ✅ | Lifetime = 2*N + 240 = **252 sec** (N=6) |
| `IMemoryCache` service | ✅ | Scoped caching, lazy load |
| Routing via `Map`, `Use`, `Run` | ✅ | `info`, `table/*`, `searchform1/2` |
| Cookies state (Form1) | ✅ | Auto-fill on reload |
| Session state (Form2) | ✅ | Object-based storage |
| Bootstrap UI | ✅ | Stylish dark theme |
| Pagination | ✅ | Query param `?page=1` |
| Search by fields | ✅ | Query form |
| Add new clients | ✅ | POST handler |
| GitHub Actions workflow | ✅ | Linux + Windows builds |

---

## 🗂 Application Endpoints

| URL | Description |
|-----|-------------|
| `/` | Home page with navigation |
| `/info` | Browser info (IP + UserAgent) |
| `/table/Clients` | Paginated Clients table |
| `/searchform1` | Form + Cookies |
| `/searchform2` | Form + Session |
| `/add/client` | Add new client |

---

## 🛠 Technologies Used

- ASP.NET Core 8 Minimal API
- Entity Framework Core (SQL Server)
- IMemoryCache
- Bootstrap 5
- GitHub Actions CI/CD

---

## 🔗 GitHub Actions Status

![build](https://github.com/dmitryspiridonovvv/lab3BD/actions/workflows/dotnet.yml/badge.svg)

---

## 📌 How to Run

```bash
dotnet restore
dotnet build
dotnet run
```

Start page automatically opens in browser ✅

---

## 🧩 Project Structure

```
LabWebApp/
 ├── Program.cs
 ├── Data/
 │    ├── FitnessContext.cs
 │    └── Client.cs
 ├── Services/
 │    └── CachedClientsService.cs
 ├── wwwroot/
 ├── .github/workflows/dotnet.yml
 └── README.md   ✅ you are here
```

---

## 🧠 State Management Summary

| Mechanism | Used in | Stored | Notes |
|----------|---------|--------|------|
| Cookies | /searchform1 | Browser | Persistent |
| Session | /searchform2 | Server | Safe for private data |

---

## 🧩 Caching Mechanism

- Key: `"Clients20"`
- IMemoryCache lifetime: `252 sec`
- Reload only after expiration

---

## 👨‍💻 Author

**Dmitry Spiridonov**
GitHub: https://github.com/dmitryspiridonovvv

---

✅ Lab Completed
🚀 Ready for checkout by instructor!

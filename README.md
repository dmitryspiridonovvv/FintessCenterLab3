# 🏋️‍♂️ Лабораторная работа №3 — ASP.NET Core Minimal API (Вариант №22)

## 📚 Описание
Простое **ASP.NET Core Web-приложение (без MVC)**, демонстрирующее:
- обработку HTTP-запросов с помощью `Map`, `Use`, `Run`;
- использование **IMemoryCache** для кэширования данных из базы;
- сохранение состояния формы с помощью **Cookies** и **Session**;
- подключение к удалённой БД (**db27595.databaseasp.net**, MONSTERASP);
- автоматическую сборку проекта под две платформы через **GitHub Actions**.

---

## ⚙️ Конфигурация
**Строка подключения:**
```csharp
Server=db27595.public.databaseasp.net;
Database=db27595;
User Id=db27595;
Password=r@8JQb_26Ad#;
Encrypt=True;
TrustServerCertificate=True;
MultipleActiveResultSets=True;

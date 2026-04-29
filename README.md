# 🚀 TenantManagement - Local Testing Guide (For Dummies!)

Welcome to the new platform! We've made it **super easy** to run this project on your Mac. You do **NOT** need Docker, Postgres, or Redis installed. Everything runs entirely locally.

Follow these simple steps to see the backend in action.

---

## 🛠 Step 1: Open Your Terminals
We need to start two different pieces of software at the same time:
1. **The API:** This handles web requests.
2. **The Worker:** This runs background tasks (like syncing Microsoft Graph data).

Because they need to run at the same time, we need **two separate terminal windows**.

1. Open your Terminal app on your Mac.
2. Press `Cmd + T` to open a second tab. Now you have two terminal windows/tabs ready!

---

## 🟢 Step 2: Start the API
Go to your **first** terminal tab and copy/paste these exact commands:

```bash
# Go into the project folder
cd "/Users/josephmasri/Library/CloudStorage/Egnyte-MasriDigital/Shared/Documents/AI Projects/SIPP/TenantManagement/src/TenantManagement.Api"

# Start the API
dotnet run
```

**What to look for:** Wait a few seconds until you see a message that says `Now listening on: http://localhost:5000` (or a similar link). Leave this window open and running!

---

## ⚙️ Step 3: Start the Background Worker
Go to your **second** terminal tab and copy/paste these exact commands:

```bash
# Go into the worker folder
cd "/Users/josephmasri/Library/CloudStorage/Egnyte-MasriDigital/Shared/Documents/AI Projects/SIPP/TenantManagement/src/TenantManagement.Worker"

# Start the Worker
dotnet run
```

**What to look for:** Wait a few seconds until you see messages saying `Hangfire Server started` and `Application started`. Leave this window open too!

---

## 👀 Step 4: Look at the Local Database!
We are using SQLite, which means the entire database is just a single file on your computer.

1. Open Finder and go to:
   `/Users/josephmasri/Library/CloudStorage/Egnyte-MasriDigital/Shared/Documents/AI Projects/SIPP/TenantManagement/src/TenantManagement.Api/`
2. You will see a file named **`TenantManagement.db`**. That is your database!
3. If you want to see the tables inside it, download a free app called **[DB Browser for SQLite](https://sqlitebrowser.org/)** or use the SQLite extension in VS Code. If you open that file, you will see the `CustomerTenants`, `TenantUsers`, and `TenantGroups` tables perfectly created!

---

## 🌐 Step 5: Test the API in your Browser
Now that everything is running, let's make sure the API is alive.

Open your favorite web browser (Chrome, Safari, etc.) and click this link:
👉 **[http://localhost:5000/api/health](http://localhost:5000/api/health)**

*(Note: If it doesn't load, look at your First Terminal tab. It will tell you exactly which port it is using, like `http://localhost:5001`. Use that number instead!)*

If you see a blank page that says `{"status":"Healthy","timestamp":"..."}`, **congratulations!** The entire backend is running flawlessly on your machine. 🎉

---

## 🛑 How to Stop Everything
When you are done testing, you need to turn the servers off.
1. Go to your **first** terminal tab and press `Control + C` on your keyboard.
2. Go to your **second** terminal tab and press `Control + C` on your keyboard.

That's it! You've successfully run the new Tenant Management platform!

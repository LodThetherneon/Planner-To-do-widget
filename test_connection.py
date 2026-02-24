import msal
import requests
import os
import atexit
import config

CACHE_FILE = "token_cache.bin"

def get_access_token():
    cache = msal.SerializableTokenCache()
    if os.path.exists(CACHE_FILE):
        cache.deserialize(open(CACHE_FILE, "r").read())
    atexit.register(lambda: open(CACHE_FILE, "w").write(cache.serialize()) if cache.has_state_changed else None)

    app = msal.PublicClientApplication(
        config.CLIENT_ID,
        authority=f"https://login.microsoftonline.com/{config.TENANT_ID}",
        token_cache=cache
    )

    accounts = app.get_accounts()
    result = None
    if accounts:
        result = app.acquire_token_silent(config.SCOPES, account=accounts[0])

    if not result:
        print("Interaktív bejelentkezés szükséges...")
        result = app.acquire_token_interactive(scopes=config.SCOPES)

    return result["access_token"] if result and "access_token" in result else None

def get_all_tasks():
    token = get_access_token()
    if not token:
        return

    headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'}

    print("\n--- 1. KERESÉS: Saját To Do feladatok (Default List) ---")
    # Ez a régi lekérdezés
    url_todo = "https://graph.microsoft.com/v1.0/me/todo/lists/tasks"
    response = requests.get(url_todo, headers=headers)
    
    if response.status_code == 200:
        tasks = response.json().get('value', [])
        print(f"Találat: {len(tasks)} db feladat")
        for t in tasks:
            print(f" - [TODO] {t['title']}")
    else:
        print("Nem sikerült lekérni a sima To Do listát.")

    print("\n--- 2. KERESÉS: 'Hozzárendelve' feladatok (Planner) ---")
    # Ez az ÚJ lekérdezés a Hozzárendelve listához
    url_planner = "https://graph.microsoft.com/v1.0/me/planner/tasks"
    response_planner = requests.get(url_planner, headers=headers)

    if response_planner.status_code == 200:
        planner_tasks = response_planner.json().get('value', [])
        print(f"Találat: {len(planner_tasks)} db feladat")
        for t in planner_tasks:
            # A Planner feladatoknál a státusz százalékos, nem csak kész/nincs kész
            percent = t.get('percentComplete', 0)
            status = "[KÉSZ]" if percent == 100 else "[FOLYAMATBAN]"
            print(f" - {status} {t['title']} (Határidő: {t.get('dueDateTime', 'Nincs')})")
    else:
        print(f"Hiba a Planner lekérésnél: {response_planner.status_code}")
        print("Lehet, hogy hiányzik a 'Group.Read.All' jogosultság, ha ezek csoportos feladatok.")

if __name__ == "__main__":
    get_all_tasks()
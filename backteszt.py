import msal
import requests
import os
import config
import urllib.parse
from datetime import datetime
import atexit

# Token gyorsítótár fájl
CACHE_FILE = "token_cache.bin"
session = requests.Session()

def get_access_token():
    """Kezeli a bejelentkezést és a token frissítését."""
    try:
        cache = msal.SerializableTokenCache()
        if os.path.exists(CACHE_FILE):
            cache.deserialize(open(CACHE_FILE, "r").read())
        
        # Automatikus mentés kilépéskor
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

        if result and "access_token" in result:
            return result["access_token"]
        return None
    except Exception as e:
        print(f"Token hiba: {e}")
        return None

def get_my_user_id():
    """Lekéri a bejelentkezett felhasználó egyedi azonosítóját."""
    token = get_access_token()
    if not token: return None
    
    url = "https://graph.microsoft.com/v1.0/me"
    headers = {'Authorization': f'Bearer {token}'}
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            return res.json().get('id')
        return None
    except Exception as e:
        print(f"User ID lekérési hiba: {e}")
        return None

def create_task(title, bucket_id, plan_id, due_date=None):
    """Új feladatot hoz létre határidővel és automatikus hozzárendeléssel."""
    token = get_access_token()
    if not token: return False

    my_id = get_my_user_id()
    if not my_id:
        print("Hiba: Nem sikerült azonosítani a felhasználót.")
        return False

    url = "https://graph.microsoft.com/v1.0/planner/tasks"
    headers = {
        'Authorization': f'Bearer {token}',
        'Content-Type': 'application/json'
    }
    
    payload = {
        "planId": plan_id,
        "bucketId": bucket_id,
        "title": title,
        "assignments": {
            my_id: {
                "@odata.type": "#microsoft.graph.plannerAssignment",
                "orderHint": " !" 
            }
        }
    }

    # Dátum hozzáadása, ha meg van adva (ISO formátum szükséges: YYYY-MM-DDThh:mm:ssZ)
    if due_date:
        payload["dueDateTime"] = f"{due_date}T12:00:00Z"

    try:
        response = session.post(url, headers=headers, json=payload, timeout=15)
        if response.status_code in [200, 201]:
            return True
        else:
            print(f"API Hiba (Create): {response.status_code} - {response.text}")
            return False
    except Exception as e:
        print(f"Hálózati hiba feladat létrehozásakor: {e}")
        return False

def update_task_completion(task_id, percent):
    token = get_access_token()
    if not token: return False
    url = f"https://graph.microsoft.com/v1.0/planner/tasks/{task_id}"
    headers = {
        'Authorization': f'Bearer {token}',
        'Content-Type': 'application/json',
        'Cache-Control': 'no-cache',
        'Prefer': 'return=representation'
    }
    try:
        get_res = session.get(url, headers={'Authorization': f'Bearer {token}', 'Cache-Control': 'no-cache'}, timeout=15)
        if get_res.status_code != 200: return False
        etag = get_res.json().get('@odata.etag')
        patch_headers = headers.copy()
        patch_headers['If-Match'] = etag
        payload = {"percentComplete": int(percent)}
        response = session.patch(url, headers=patch_headers, json=payload, timeout=15)
        return response.status_code in [200, 204]
    except Exception as e:
        print(f"Update hiba: {e}")
        return False

def complete_task(task_id, *args): return update_task_completion(task_id, 100)
def reopen_task(task_id, *args): return update_task_completion(task_id, 0)

def delete_task(task_id):
    """Remove a planner task (also affects To Do since they share the same underlying item).
    The Graph API requires an `If-Match` header containing the task's ETag.
    """
    token = get_access_token()
    if not token:
        return False
    url = f"https://graph.microsoft.com/v1.0/planner/tasks/{task_id}"
    headers = {'Authorization': f'Bearer {token}', 'Cache-Control': 'no-cache'}
    try:
        # fetch etag first
        res = session.get(url, headers=headers, timeout=15)
        if res.status_code != 200:
            print(f"Delete prep failed, status={res.status_code}")
            return False
        etag = res.json().get('@odata.etag')
        if not etag:
            print("Missing ETag for delete")
            return False
        del_headers = {'Authorization': f'Bearer {token}', 'If-Match': etag}
        del_res = session.delete(url, headers=del_headers, timeout=15)
        return del_res.status_code in [204]
    except Exception as e:
        print(f"Delete hiba: {e}")
        return False

def fetch_data():
    try:
        token = get_access_token()
        if not token: return {"error": "Nincs bejelentkezve"}
        headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json', 'Cache-Control': 'no-cache'}
        url_planner = "https://graph.microsoft.com/v1.0/me/planner/tasks"
        response = session.get(url_planner, headers=headers, timeout=15)
        if response.status_code == 200:
            tasks = response.json().get('value', [])
            formatted_tasks = []
            for t in tasks:
                p_val = t.get('priority', 5)
                if p_val <= 2: priority = 'high'
                elif p_val <= 4: priority = 'high'
                elif p_val <= 7: priority = 'medium'
                else: priority = 'low'
                formatted_tasks.append({
                    "id": t.get('id'),
                    "title": t['title'],
                    "status": "KESZ" if t.get('percentComplete') == 100 else "FOLYAMATBAN",
                    "date": t.get('dueDateTime', 'Nincs határidő')[:10] if t.get('dueDateTime') else "Nincs határidő",
                    "priority": priority
                })
            return formatted_tasks
        return {"error": f"API Hiba: {response.status_code}"}
    except Exception as e:
        return {"error": str(e)}
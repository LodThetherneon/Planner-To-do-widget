# backend.py
import msal
import requests
import os
import config
import atexit
import ctypes
import json
from ctypes import wintypes

CACHEFILE = "tokencache.bin"
PLAN_CACHE_FILE = "plan_cache.json"
session = requests.Session()


class DATABLOB(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD),
                ("pbData", ctypes.POINTER(ctypes.c_byte))]


def dpapi_protect(data: bytes) -> bytes:
    if os.name != "nt":
        return data
    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    inblob = DATABLOB(len(data), ctypes.cast(ctypes.create_string_buffer(data), ctypes.POINTER(ctypes.c_byte)))
    outblob = DATABLOB()
    if not crypt32.CryptProtectData(ctypes.byref(inblob), None, None, None, None, 0, ctypes.byref(outblob)):
        raise ctypes.WinError()
    try:
        out = ctypes.string_at(outblob.pbData, outblob.cbData)
    finally:
        kernel32.LocalFree(outblob.pbData)
    return out


def dpapi_unprotect(data: bytes) -> bytes:
    if os.name != "nt":
        return data
    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    inblob = DATABLOB(len(data), ctypes.cast(ctypes.create_string_buffer(data), ctypes.POINTER(ctypes.c_byte)))
    outblob = DATABLOB()
    if not crypt32.CryptUnprotectData(ctypes.byref(inblob), None, None, None, None, 0, ctypes.byref(outblob)):
        raise ctypes.WinError()
    try:
        out = ctypes.string_at(outblob.pbData, outblob.cbData)
    finally:
        kernel32.LocalFree(outblob.pbData)
    return out


def _load_cache_text():
    if not os.path.exists(CACHEFILE):
        return None
    try:
        raw = open(CACHEFILE, "rb").read()
        if not raw:
            return None
        if os.name == "nt":
            try:
                raw = dpapi_unprotect(raw)
            except Exception:
                pass
        return raw.decode("utf-8", errors="replace")
    except Exception:
        return None


def _save_cache_text(text: str) -> None:
    raw = (text or "").encode("utf-8")
    if os.name == "nt":
        try:
            raw = dpapi_protect(raw)
        except Exception:
            pass
    with open(CACHEFILE, "wb") as f:
        f.write(raw)


def _load_plan_cache() -> dict:
    if not os.path.exists(PLAN_CACHE_FILE):
        return {}
    try:
        with open(PLAN_CACHE_FILE, "r", encoding="utf-8") as f:
            return json.load(f) or {}
    except Exception:
        return {}


def _save_plan_cache(cache_data: dict) -> None:
    try:
        with open(PLAN_CACHE_FILE, "w", encoding="utf-8") as f:
            json.dump(cache_data, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def _msal_app_and_cache():
    cache = msal.SerializableTokenCache()
    cache_text = _load_cache_text()
    if cache_text:
        cache.deserialize(cache_text)

    atexit.register(lambda: _save_cache_text(cache.serialize()) if cache.has_state_changed else None)

    app = msal.PublicClientApplication(
        config.CLIENT_ID,
        authority=f"https://login.microsoftonline.com/{config.TENANT_ID}",
        token_cache=cache
    )
    return app, cache


def get_access_token_silent():
    try:
        app, _cache = _msal_app_and_cache()
        accounts = app.get_accounts()
        if not accounts:
            return None
        result = app.acquire_token_silent(config.SCOPES, account=accounts[0])
        if result and "access_token" in result:
            return result["access_token"]
        return None
    except Exception as e:
        print(f"Silent token hiba: {e}")
        return None


def get_access_token_interactive():
    try:
        app, cache = _msal_app_and_cache()
        result = app.acquire_token_interactive(scopes=config.SCOPES)
        if result and "access_token" in result:
            if cache.has_state_changed:
                _save_cache_text(cache.serialize())
            return result["access_token"]
        return None
    except Exception as e:
        print(f"Interactive token hiba: {e}")
        return None


def _planner_api_call(method: str, endpoint: str, payload=None, needs_etag=False):
    """Közös segédfüggvény a Planner API hívásokhoz. Jelentősen csökkenti a kód ismétlődését."""
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve", None

    url = f"https://graph.microsoft.com/v1.0{endpoint}"
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
        "Cache-Control": "no-cache"
    }

    try:
        if needs_etag:
            get_res = session.get(url, headers={"Authorization": f"Bearer {token}", "Cache-Control": "no-cache"}, timeout=15)
            if get_res.status_code != 200:
                return False, f"ETag hiba: {get_res.status_code} - {get_res.text}", None
            etag = get_res.json().get("@odata.etag")
            if not etag:
                return False, "Hiányzó ETag", None
            headers["If-Match"] = etag
            headers["Prefer"] = "return=representation"

        if method == "GET":
            res = session.get(url, headers=headers, timeout=15)
        elif method == "POST":
            res = session.post(url, headers=headers, json=payload, timeout=15)
        elif method == "PATCH":
            res = session.patch(url, headers=headers, json=payload, timeout=15)
        elif method == "DELETE":
            res = session.delete(url, headers=headers, timeout=15)
        else:
            return False, "Ismeretlen metódus", None

        if res.status_code in (200, 201, 204):
            return True, "", res
        return False, f"API hiba: {res.status_code} - {res.text}", res
    except Exception as e:
        return False, f"Hálózati hiba: {e}", None


def get_my_user_id(token: str):
    if not token:
        return None
    url = "https://graph.microsoft.com/v1.0/me"
    headers = {"Authorization": f"Bearer {token}"}
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            return res.json().get("id")
        return None
    except Exception as e:
        print(f"User ID lekérési hiba: {e}")
        return None


def get_my_display_name(token: str | None = None) -> str | None:
    if not token:
        token = get_access_token_silent()
    if not token:
        return None
    url = "https://graph.microsoft.com/v1.0/me"
    headers = {"Authorization": f"Bearer {token}"}
    try:
        res = session.get(url, headers=headers, timeout=10)
        if res.status_code == 200:
            return (res.json().get("displayName") or "").strip() or None
        return None
    except Exception as e:
        print(f"DisplayName lekérési hiba: {e}")
        return None


def list_my_plans():
    plans_dict = {}
    plan_cache = _load_plan_cache()
    cache_updated = False

    # 1. Hivatalos végpont (csak a Kedvencek és a saját tulajdonú tervek jönnek le)
    ok_plans, msg_plans, res_plans = _planner_api_call("GET", "/me/planner/plans")
    if ok_plans and res_plans:
        for p in res_plans.json().get("value", []):
            pid = p.get("id")
            title = p.get("title", "")
            if pid:
                plans_dict[pid] = {"id": pid, "title": title}
                # Ha új nevet találunk vagy még nem volt a cache-ben, frissítsük
                if plan_cache.get(pid) != title:
                    plan_cache[pid] = title
                    cache_updated = True

    # 2. Biztonsági háló: Kigyűjtjük a Plan ID-kat a user meglévő feladataiból
    ok_tasks, msg_tasks, res_tasks = _planner_api_call("GET", "/me/planner/tasks")
    if ok_tasks and res_tasks:
        for t in res_tasks.json().get("value", []):
            pid = t.get("planId")
            # Ha a feladat egy olyan tervhez tartozik, ami eddig nem volt a hivatalos listában:
            if pid and pid not in plans_dict:
                # 3. Ellenőrizzük a helyi gyorsítótárat (cache), hogy spóroljunk egy API hívást
                if pid in plan_cache:
                    plans_dict[pid] = {"id": pid, "title": plan_cache[pid]}
                else:
                    # Ha nincs meg a cache-ben sem, csak akkor hívjuk a Graph API-t egyedileg
                    ok_single, msg_single, res_single = _planner_api_call("GET", f"/planner/plans/{pid}")
                    if ok_single and res_single:
                        p_data = res_single.json()
                        title = p_data.get("title", "(Névtelen Terv)")
                        plans_dict[pid] = {"id": pid, "title": title}
                        plan_cache[pid] = title
                        cache_updated = True

    # Kimentjük a frissített cache-t, ha volt változás
    if cache_updated:
        _save_plan_cache(plan_cache)

    out = list(plans_dict.values())
    if out:
        return True, out
    return False, "Nem található egyetlen terv sem (esetleg hiányzó jogosultság)."


def list_buckets_for_plan(plan_id: str):
    ok, msg, res = _planner_api_call("GET", f"/planner/plans/{plan_id}/buckets")
    if not ok:
        return False, msg
    items = res.json().get("value", []) or []
    return True, [{"id": x.get("id", ""), "name": x.get("name", "")} for x in items if x.get("id")]


def create_task(title, bucket_id, plan_id, due_date=None):
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"
    
    my_id = get_my_user_id(token)
    if not my_id:
        return False, "Nem sikerült azonosítani a felhasználót"

    payload = {
        "planId": plan_id,
        "bucketId": bucket_id,
        "title": title,
        "assignments": {
            my_id: {
                "@odata.type": "microsoft.graph.plannerAssignment",
                "orderHint": " !"
            }
        }
    }
    if due_date:
        payload["dueDateTime"] = f"{due_date}T12:00:00Z"

    ok, msg, _ = _planner_api_call("POST", "/planner/tasks", payload=payload)
    return ok, msg


def update_task_completion(task_id, percent):
    payload = {"percentComplete": int(percent)}
    ok, msg, _ = _planner_api_call("PATCH", f"/planner/tasks/{task_id}", payload=payload, needs_etag=True)
    return ok, msg


def update_task_details(task_id, new_title=None, new_due_date=None):
    payload = {}
    if new_title is not None:
        payload["title"] = new_title
    if new_due_date is not None:
        if new_due_date == "Nincs határidő" or not new_due_date:
            payload["dueDateTime"] = None
        else:
            payload["dueDateTime"] = f"{new_due_date}T12:00:00Z"
            
    if not payload:
        return True, "No changes"
        
    ok, msg, _ = _planner_api_call("PATCH", f"/planner/tasks/{task_id}", payload=payload, needs_etag=True)
    return ok, msg


def complete_task(task_id, *args):
    return update_task_completion(task_id, 100)


def reopen_task(task_id, *args):
    return update_task_completion(task_id, 0)


def delete_task(task_id):
    ok, msg, _ = _planner_api_call("DELETE", f"/planner/tasks/{task_id}", needs_etag=True)
    return ok, msg


def _planner_priority_to_level(p_val: int) -> str:
    if p_val <= 1:
        return "urgent"
    if p_val <= 4:
        return "important"
    if p_val <= 7:
        return "medium"
    return "low"


def fetch_data():
    ok, msg, res = _planner_api_call("GET", "/me/planner/tasks")
    if not ok:
        return {"error": msg}

    tasks = res.json().get("value", [])
    formatted_tasks = []

    for t in tasks:
        raw = t.get("priority", None)
        try:
            p_val = int(raw) if raw is not None else 5
        except Exception:
            p_val = 5
        level = _planner_priority_to_level(p_val)

        formatted_tasks.append({
            "id": t.get("id"),
            "title": t.get("title", ""),
            "status": "KESZ" if t.get("percentComplete") == 100 else "FOLYAMATBAN",
            "date": t.get("dueDateTime", "Nincs határidő")[:10] if t.get("dueDateTime") else "Nincs határidő",
            "priority": level,
        })

    return formatted_tasks

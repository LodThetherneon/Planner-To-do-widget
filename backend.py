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
        if res.status_code != 200:
            return None
        data = res.json() or {}
        name = (data.get("displayName") or "").strip()
        return name or None
    except Exception as e:
        print(f"DisplayName lekérési hiba: {e}")
        return None


def list_my_plans():
    # GET /me/planner/plans [web:268]
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"
    url = "https://graph.microsoft.com/v1.0/me/planner/plans"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    try:
        res = session.get(url, headers=headers, timeout=15)
        if res.status_code == 200:
            items = res.json().get("value", []) or []
            out = [{"id": x.get("id", ""), "title": x.get("title", "")} for x in items]
            out = [x for x in out if x["id"]]
            return True, out
        return False, f"Plans API hiba: {res.status_code} - {res.text}"
    except Exception as e:
        return False, f"Hálózati hiba: {e}"


def list_buckets_for_plan(plan_id: str):
    # GET /planner/plans/{plan-id}/buckets [web:261]
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"
    url = f"https://graph.microsoft.com/v1.0/planner/plans/{plan_id}/buckets"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    try:
        res = session.get(url, headers=headers, timeout=15)
        if res.status_code == 200:
            items = res.json().get("value", []) or []
            out = [{"id": x.get("id", ""), "name": x.get("name", "")} for x in items]
            out = [x for x in out if x["id"]]
            return True, out
        return False, f"Buckets API hiba: {res.status_code} - {res.text}"
    except Exception as e:
        return False, f"Hálózati hiba: {e}"


def create_task(title, bucket_id, plan_id, due_date=None):
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"

    my_id = get_my_user_id(token)
    if not my_id:
        return False, "Nem sikerült azonosítani a felhasználót"

    url = "https://graph.microsoft.com/v1.0/planner/tasks"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
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

    try:
        response = session.post(url, headers=headers, json=payload, timeout=15)
        if response.status_code in (200, 201):
            return True, ""
        return False, f"Create API hiba: {response.status_code} - {response.text}"
    except Exception as e:
        return False, f"Hálózati hiba: {e}"


def update_task_completion(task_id, percent):
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"

    url = f"https://graph.microsoft.com/v1.0/planner/tasks/{task_id}"
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
        "Cache-Control": "no-cache",
        "Prefer": "return=representation"
    }

    try:
        get_res = session.get(url, headers={"Authorization": f"Bearer {token}", "Cache-Control": "no-cache"}, timeout=15)
        if get_res.status_code != 200:
            return False, f"ETag lekérés hiba: {get_res.status_code} - {get_res.text}"

        etag = get_res.json().get("@odata.etag")
        if not etag:
            return False, "Hiányzó ETag"

        patch_headers = headers.copy()
        patch_headers["If-Match"] = etag
        payload = {"percentComplete": int(percent)}
        response = session.patch(url, headers=patch_headers, json=payload, timeout=15)

        if response.status_code in (200, 204):
            return True, ""
        return False, f"Update API hiba: {response.status_code} - {response.text}"
    except Exception as e:
        return False, f"Update hiba: {e}"


def update_task_details(task_id, new_title=None, new_due_date=None):
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"

    url = f"https://graph.microsoft.com/v1.0/planner/tasks/{task_id}"
    
    # First get ETag
    headers = {"Authorization": f"Bearer {token}", "Cache-Control": "no-cache"}
    try:
        res = session.get(url, headers=headers, timeout=15)
        if res.status_code != 200:
             return False, f"ETag error: {res.status_code}"
        
        etag = res.json().get("@odata.etag")
        if not etag:
            return False, "Missing ETag"
        
        patch_headers = {
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "If-Match": etag,
            "Prefer": "return=representation"
        }
        
        payload = {}
        if new_title is not None:
            payload["title"] = new_title
        if new_due_date is not None:
            if new_due_date == "Nincs határidő" or not new_due_date:
                payload["dueDateTime"] = None
            else:
                 # Validate format YYYY-MM-DD
                 payload["dueDateTime"] = f"{new_due_date}T12:00:00Z"
        
        if not payload:
             return True, "No changes"

        response = session.patch(url, headers=patch_headers, json=payload, timeout=15)
        if response.status_code in (200, 204):
            return True, ""
        return False, f"Update failed: {response.status_code} - {response.text}"

    except Exception as e:
        return False, f"Exception: {e}"


def complete_task(task_id, *args):
    return update_task_completion(task_id, 100)


def reopen_task(task_id, *args):
    return update_task_completion(task_id, 0)


def delete_task(task_id):
    token = get_access_token_silent()
    if not token:
        return False, "Nincs bejelentkezve"

    url = f"https://graph.microsoft.com/v1.0/planner/tasks/{task_id}"
    headers = {"Authorization": f"Bearer {token}", "Cache-Control": "no-cache"}

    try:
        res = session.get(url, headers=headers, timeout=15)
        if res.status_code != 200:
            return False, f"Delete prep failed: {res.status_code} - {res.text}"

        etag = res.json().get("@odata.etag")
        if not etag:
            return False, "Missing ETag for delete"

        del_headers = {"Authorization": f"Bearer {token}", "If-Match": etag}
        del_res = session.delete(url, headers=del_headers, timeout=15)
        if del_res.status_code in (204,):
            return True, ""
        return False, f"Delete API hiba: {del_res.status_code} - {del_res.text}"
    except Exception as e:
        return False, f"Delete hiba: {e}"


def _planner_priority_to_level(p_val: int) -> str:
    if p_val <= 1:
        return "urgent"
    if p_val <= 4:
        return "important"
    if p_val <= 7:
        return "medium"
    return "low"


def fetch_data():
    try:
        token = get_access_token_silent()
        if not token:
            return {"error": "Nincs bejelentkezve"}

        headers = {
            "Authorization": "Bearer " + token,
            "Content-Type": "application/json",
            "Cache-Control": "no-cache"
        }

        url_planner = "https://graph.microsoft.com/v1.0/me/planner/tasks"
        response = session.get(url_planner, headers=headers, timeout=15)

        if response.status_code == 200:
            tasks = response.json().get("value", [])
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

        return {"error": f"API Hiba: {response.status_code} - {response.text}"}

    except Exception as e:
        return {"error": str(e)}

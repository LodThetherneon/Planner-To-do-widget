# config.py
# MSAL / Microsoft Graph config (used by backend.py)

# Azure App Registration (Entra ID) Application (client) ID
CLIENT_ID = "34e2c374-eb9b-4a30-9f19-879117b91660"

# Directory (tenant) ID, vagy "common" / "organizations" (ha úgy van beállítva az app)
TENANT_ID = "ba3bfa82-3437-46e6-815c-d987814eeaee"

# Microsoft Graph scopes
# Planner olvasáshoz jellemzően kell: Tasks.Read (vagy a megfelelő delegated permission),
# plusz ha létrehozol/módosítasz: Tasks.ReadWrite.
SCOPES = [
    "User.Read",
    "Tasks.ReadWrite",
]

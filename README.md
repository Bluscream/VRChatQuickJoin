# VRChatQuickJoin

## Installation
1. Download from https://github.com/Bluscream/VRChatQuickJoin/releases/latest/download/VRChatQuickJoin.exe
2. Put in VRChat folder (Example: `C:\Program Files (x86)\Steam\steamapps\common\VRChat\`)
3. Put shortcut on desktop or taskbar
4. Launch once and let it generate `VRChatQuickJoin.json`
5. Edit `VRChatQuickJoin.json` with your credentials, users, groups and or worlds
6. Launch again and Profit!
<hr>

## Example `VRChatQuickJoin.json`
```json
{
  "WaitOnExit": true,
  "FetchGroupDetails": true,
  "OverwriteComments": true,
  "Username": "",
  "Password": "",
  "TOTPSecret": "",
  "Ids": [
    "usr_9ebcb36c-d65b-4e3f-99ca-14b8c73eacdf": "My cool friend",
    "grp_24b93850-e60f-4581-8a5d-c7e529f02574": "My cool group",
    "wrld_867805a3-a057-43a7-84f3-ba1b4e6ca488": "My cool world"
  ],
  "AuthCookie": "",
  "TwoFactorAuthCookie": "",
  "GameArguments": "--no-vr",
  "LaunchMode": 1
}
```

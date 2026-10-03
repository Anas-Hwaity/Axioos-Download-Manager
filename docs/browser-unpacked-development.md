# Unpacked Chromium/Edge development extension

The source Chromium extension uses a checked-in public development key so **Load unpacked** has the deterministic development extension ID:

`bobopmadclgeepbcnddbeoaalhfkljep`

This identity is separate from the inherited official/store extension ID `akdmdglbephckgfmdffcdebnpjgamofc`. The native-host manifest accepts only these two known origins; it does not allow arbitrary extensions.

For development, load `ADM/chrome-extension` directly in Edge/Chrome Developer mode. The source manifest key keeps the ID stable. `tools/prepare_unpacked_extension.py` remains available for producing a clean copied development extension and identity metadata. `tools/register_unpacked_native_host.py` registers a per-user development host manifest while preserving the official origin in that manifest.

The `adm-app://` protocol launch fallback remains restricted to the official extension identity. Development/unpacked builds use native messaging and must not gain a global protocol-launch privilege.

# Proxmark3 client reference (vendored)

Upstream Iceman/RRG client documentation, copied here so the GUI's command usage and output parsers can be checked against the real client behaviour.

| File | Use in this project |
| :--- | :--- |
| `commands.md` / `commands.json` | Full client command reference with output samples and help text. Start here when changing a command invocation or parser. |
| `cheatsheet.md` | Condensed command list. |
| `magic_cards_notes.md` | Gen 1a / Gen 2 / CUID magic-card behaviour behind the UID writer. |
| `mfc_notes.md` | MIFARE Classic internals behind `hf mf chk` / `rdbl` / `wrbl`. |
| `T5577_Guide.md`, `cloner_notes.md` | LF T55xx and card-cloning notes. |
| `jtag_notes.md` | Recovery guide linked from the flasher UI. |
| `standalone/` | Standalone-mode command docs (not used by the GUI). |
| `new_frame_format.md`, `uart_notes.md`, `clocks.md` | USB/serial framing and timing internals. |

Version caveat: these files track upstream `main`, while the shipped client is a snapshot in `client/`. When a parse breaks, trust the client's own output — captured samples live in `client/.proxmark3/logs/` and `dumps/` — and treat the docs as intent, not ground truth.

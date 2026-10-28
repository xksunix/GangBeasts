# Gang Beasts Guns 1.0 (MelonLoader, IL2CPP)

Każdy ludzik dostaje pistolet (z sześcianów, wskazuje w stronę celowania). Można strzelać, trafiać w głowę, urywać kończyny i zabijać.

## Instalacja
Wrzuć `GangBeastsGuns.dll` do folderu `Mods` w katalogu gry i uruchom grę.

## Sterowanie
| Co | Mysz / klawiatura | Pad |
|---|---|---|
| Celowanie | kursor myszy (czerwona kropka pokazuje cel) | prawy drążek |
| Strzał | LPM lub PPM (przyciski bicia) | X lub B (bicie) |
| Zmień, która postać jest Twoja | F5 | |
| Włącz / wyłącz mod | F6 | |
| Inni strzelają wł./wył. | F7 | |

## Zasady
- Zdrowie 100, strzał w ciało 25 (4 strzały), w głowę x2 (2 strzały).
- 2 trafienia w tę samą rękę/nogę urywają ją (staw znika).
- Zabity ludzik rozpada się na kawałki, przestaje się podnosić, a pistolet wypada.
- Inne ludziki (boty/koledzy) też strzelają do najbliższego celu - wyłączysz w F7.

## Ustawienia
`UserData/MelonPreferences.cfg`, sekcja `GBGuns`: `Health`, `Damage`, `HeadshotMultiplier`, `FireInterval`, `Range`, `Knockback`, `OthersShoot`, `OthersSpreadDegrees`, `OthersShootInterval`, `HitsToTearOffLimb`, `Dismember`, `GunSize`, `StickDeadzone`, `PlayerIndex`, `PadFireButtons` (nazwy przycisków Input System, np. `buttonWest,buttonEast,rightShoulder`), `Volume`, `ShowAimDot`, `ForceCursor`.

## Uwaga
Mod wykrywa "swoją" postać zgadując (pole typu isLocal/isPlayer); jeśli strzela ktoś inny, naciśnij F5, aż będzie Twoja. Strzela jedna postać sterowana tym komputerem (mysz albo pierwszy pad).

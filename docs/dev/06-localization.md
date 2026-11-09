# 6. Localization

The interface speaks Portuguese (the default, written in the code), English, Russian and Simplified Chinese.

## `Loc.T(key, portuguese)`

Every text the user can see goes through `UI/Loc.cs`:

```csharp
_status.Text = Loc.T("terrain.picked", "Pincel com a textura daqui: {0}");
```

- The Portuguese text in the code is the default and the fallback.
- The translations are in `lang/en.json`, `lang/ru.json`, `lang/zh-CN.json` — flat `"key": "text"` maps that ship
  next to the `.exe`, so the community can fix a translation without recompiling.
- A new key must be added to the three files in the same change. Keep `{0}` placeholders in the same places.
- The language is chosen in the app (top right) and stored in `%AppData%\MyAceEditor`; `MYACE_LANG=en` forces a
  language for one run without saving it (used by tests and screenshots).

Screens rebuild their texts when `Loc.Changed` fires (the language was switched).

## Game texture names

Ground textures, grass and trees are named by their file path in the game packages, usually in Chinese. The app shows
a translated name built from `lang/texture-words.json` (Chinese word → Portuguese, English, Russian) by
`TextureNameGlossary`. The user can rename one (F2); the new name is stored per client and per language. The file
itself is never renamed: the game finds textures by path.

## No cut text

Translated texts are often longer than the Portuguese. Big tool buttons shrink their font until every word fits
(`FitBigText`); small buttons grow to two lines. The command

```bash
set MYACE_LANG=ru
MyAceEditor.exe --world-shot "<client>\element\maps\world" <out> "1100,240,1040,0,-0.5,dftlayout"
```

walks every button, label and checkbox of the 3D World (hidden panels included) and lists any text that does not fit.
It must report **0** in `en`, `ru`, `zh` and `pt` before a UI change is done.

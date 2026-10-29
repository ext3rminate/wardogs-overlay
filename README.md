# Wardogs Overlay

[English](#english) · [Русский](#русский)

## English

An overlay for **Wardogs** with weapon and attachment stats in the build menu, a build planner, an item database, and session economy tracking. The interface supports English and Russian.

### Installation

1. Open [Releases](https://github.com/ext3rminate/wardogs-overlay/releases) and download `WardogsOverlay-v…-win-x64.zip`.
2. Extract the archive to a writable folder. Do not run the application directly from the ZIP.
3. Run `WardogsOverlay.exe`. Release packages include the .NET Runtime.
4. Install the Russian language in Windows for text recognition. Set the game interface to Russian and use borderless windowed mode.

Closing the window minimizes the application to the system tray. To quit completely, use the tray icon menu. On first launch, the application downloads data and icons from [MetaForge](https://metaforge.app/wardogs).

To update, quit the application through the tray menu and extract the new version over the old one. Personal settings and session history are stored locally.

### Features

- Stats for the selected weapon and installed attachments, with comparison against a saved build.
- Compatible attachments for the selected slot, including prices and resulting stat changes.
- Build recommendations based on budget and priority: balance, recoil, or aiming speed.
- Weapons, attachments, armour, and ammunition database; unlock progression and game patch changes.
- Session income and spending, a balance chart, and session history.
- Adjustable overlay size, opacity, and position, with Windows display scaling support.

The application uses screenshots, Windows text recognition, and a regular overlay window. It does not read game memory, inject code, or modify game files. Anti-cheat decisions remain with the game developers.

### Hotkeys

These shortcuts work while the game is in the foreground.

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+O | Toggle menu detection |
| Ctrl+Shift+S | Save a build or reset the comparison |
| Ctrl+Shift+B | Cycle build selection priorities |
| Ctrl+Shift+H | Show or hide slot options |
| Ctrl+Shift+M | Move the overlay |
| Ctrl+Shift+D | Save a debug screenshot |

Full instructions: [docs/USER_GUIDE.txt](docs/USER_GUIDE.txt), with English first and a Russian translation below.

### Building from source

Requires Windows 10 build 19041 or later and the .NET 10 SDK.

```powershell
dotnet build WardogsOverlay/WardogsOverlay.csproj -c Release
```

To create a Windows x64 release ZIP:

```powershell
./scripts/Publish-Release.ps1 -Version 1.0.0
```

The script writes the package, a standalone EXE, and checksums to `artifacts/`. For a smaller package that requires an installed .NET 10 Desktop Runtime, add `-FrameworkDependent`.

### Checking the interface without the game

Database page screenshots require downloaded `data/` files next to the executable. To test recognition using a screenshot of the game menu:

```powershell
./artifacts/WardogsOverlay-v1.0.0-win-x64/WardogsOverlay.exe --test screenshot.png
```

### Publishing a new version

GitHub Actions checks the build on pushes to `main` and on pull requests. To publish a version, update `<Version>` in the project, commit the change, and push a matching tag:

```powershell
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

The **Release** workflow validates the version, builds the application, and publishes a GitHub Release with ZIP, EXE, and SHA-256 checksums. You can also run the workflow manually from Actions using an existing tag. README files and release notes use English first, followed by the Russian translation.

### Data and third-party components

Game data and icons are downloaded from MetaForge and are not included in the source repository or release package. Onest and JetBrains Mono fonts are distributed under the SIL Open Font License. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## Русский

Оверлей для **Wardogs**: характеристики оружия и обвесов в меню сборки, подбор сборок, база предметов и экономика сессии. Интерфейс на русском и английском.

### Установка

1. Открой [Releases](https://github.com/ext3rminate/wardogs-overlay/releases) и скачай `WardogsOverlay-v…-win-x64.zip`.
2. Распакуй архив в отдельную папку с правом записи. Не запускай программу прямо из ZIP.
3. Запусти `WardogsOverlay.exe`. Релизная сборка включает .NET Runtime.
4. В Windows установи русский язык для распознавания текста. В игре выбери русский интерфейс и оконный режим без рамки.

Крестик сворачивает приложение в трей. Полный выход — через меню значка рядом с часами. При первом запуске программа скачивает данные и иконки с [MetaForge](https://metaforge.app/wardogs).

При обновлении закрой приложение через трей и распакуй новую версию поверх старой. Личные настройки и история сессий создаются локально.

### Возможности

- Характеристики выбранной пушки и установленных обвесов, сравнение с сохранённой сборкой.
- Варианты обвесов для выбранного слота, цены и итоговые эффекты.
- Подбор сборок по бюджету и приоритету: баланс, отдача или скорость прицеливания.
- База оружия, обвесов, брони и патронов; прогресс открытия предметов и изменения патча.
- Доходы и расходы за сессию, график баланса и история.
- Настройка размера, непрозрачности и положения плашки; поддержка масштаба Windows.

Программа использует снимки экрана, системное распознавание текста и обычное окно поверх игры. Она не читает память игры, не внедряет код и не изменяет файлы игры. Решения античита остаются на стороне разработчиков игры.

### Горячие клавиши

Работают, пока игра на переднем плане.

| Клавиши | Действие |
| --- | --- |
| Ctrl+Shift+O | Включить или выключить поиск меню |
| Ctrl+Shift+S | Сохранить сборку или сбросить сравнение |
| Ctrl+Shift+B | Переключить приоритет подбора сборки |
| Ctrl+Shift+H | Показать или скрыть варианты слота |
| Ctrl+Shift+M | Переместить плашку |
| Ctrl+Shift+D | Сохранить отладочный снимок |

Подробная инструкция: [docs/USER_GUIDE.txt](docs/USER_GUIDE.txt), сначала на английском, затем в переводе на русский.

### Сборка из исходников

Нужны Windows 10 версии 19041 или новее и .NET 10 SDK.

```powershell
dotnet build WardogsOverlay/WardogsOverlay.csproj -c Release
```

Создание ZIP со сборкой для Windows x64:

```powershell
./scripts/Publish-Release.ps1 -Version 1.0.0
```

Скрипт сохраняет пакет, отдельный EXE и контрольные суммы в `artifacts/`. Для меньшего пакета, требующего установленного .NET 10 Desktop Runtime, добавь `-FrameworkDependent`.

### Проверка интерфейса без игры

Для снимков страниц базы нужны скачанные данные `data/` рядом с исполняемым файлом. Проверку распознавания можно запустить на снимке меню игры:

```powershell
./artifacts/WardogsOverlay-v1.0.0-win-x64/WardogsOverlay.exe --test screenshot.png
```

### Выпуск новой версии

Сборка проверяется GitHub Actions при изменениях в `main` и в pull request. Для выпуска новой версии обнови `<Version>` в проекте, сделай коммит и отправь тег с тем же номером:

```powershell
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

Workflow **Release** проверит версию, соберёт приложение и опубликует GitHub Release с ZIP, EXE и SHA-256. Сборку и релиз также можно запустить вручную из Actions, указав существующий тег. В README и заметках о релизе сначала идёт английский текст, затем перевод на русский.

### Данные и сторонние компоненты

Данные игры и иконки загружаются с MetaForge и не включены в исходники и релизный пакет. Шрифты Onest и JetBrains Mono распространяются по SIL Open Font License. Подробности: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

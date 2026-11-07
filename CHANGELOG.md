# Changelog

## English

### Unreleased

- Game data refreshes automatically from MetaForge at startup (if not checked within an hour) and every 6 hours, and is swapped in without a restart. When a new game patch appears, the main window shows a banner and the tray shows a notification.
- New "Combat: damage & TTK" page: shots and time to kill by hit zone, body armour, helmet, distance and ammo type (FMJ/HP/AP), with damage per hit, magazine cost, ammo unlock and a recommendation on which ammo to buy. The model matches MetaForge's calculator, including range falloff and bullet travel time.
- The panel shows shots to kill against the chosen enemy armour and distance (chest, head, best ammo), and the life cost — the build plus your armour and helmet — with how many rebuys your balance covers (red below three). Both are set on the main window.
- Economy page: "Where the money went" — purchases detected from vendor balance drops within one visit and attributed to the weapon or attachments that appeared (otherwise ammo, armour or gear), with totals by kind; "Goal" — pick any item with an unlock cost to see how much is missing and how long to play at your average rate, plus the role level it needs.
- The life-cost line on the panel shows payback time: minutes of play at your average income to earn the build back.
- Fixed empty space under page headers in the database window.
- Shotgun and time-to-kill estimates across the app now use the same armour model (per-tier reduction, ammo penetration and armour rules).
- Patch page: official developer posts from Steam since the previous patch, a "My weapons" filter (weapons the overlay has recognized and their attachments), removed items, the game build number, and every change per item (previously cut at six).

### 1.1.1 — 2026-11-07

- The game menu is recognized in both the English and the Russian game interface. Windows no longer needs the Russian language installed: without it, Russian labels are matched by letter shapes as read by the English OCR engine.
- English UI details: "ATTC." and "AMMO" buttons, "ATTACHMENTS" header (or its usual position if unreadable), and balances like "$12,500".

### 1.1.0 — 2026-11-06

- Update check from GitHub Releases at startup and every 6 hours, with an update banner (Update, What's new, Skip this version) and an "Update automatically" toggle on the main window. Downloads are verified with SHA256SUMS.txt; automatic installs wait until the game is in the background.
- Shooting-range balance ($10 000) no longer counts as income; balance jumps are treated as a new baseline, and earnings per minute skip them.
- Balance reading tolerates OCR look-alike letters and a detached "$", and returns nothing instead of a value 1000x too small.
- When a game tooltip covers attachment cells, the overlay keeps the previous result instead of showing unknown attachments; a changed build is shown only after two matching frames.
- "Start minimized to tray" works again.
- The app no longer exits when offline if some icons or the patch log are missing; downloaded data is validated before replacing the cache, and icon and settings/economy files are written atomically.
- Fixed a control leak on the Patch and Budget loadout pages.
- Shotgun damage counts all pellets and the buckshot armour penalty.
- A canted sight no longer overrides the main sight's zoom; very large budgets no longer slow down the planner.
- Weapon detection by icon needs a clear margin or two matching frames; price matches are checked against the icon.
- Settings values are validated on load; a corrupt settings or economy file is kept as `.corrupt` instead of being overwritten.
- `--test --self-check` runs logic checks without the game.

### 1.0.0 — 2026-10-29

- Initial release of Wardogs Overlay.
- Weapon and attachment stats, slot options, and build comparison.
- Budget build planner, item database, unlock progression, session economy, and game patch history.
- English and Russian interface, hotkeys, and overlay settings.
- Fixed Windows DPI scaling, enlarged fonts and table rows, and corrected clipped labels.
- Automated builds and release publishing through GitHub Actions.

---

## Русский

### Не выпущено

- База сама обновляется с MetaForge при запуске (если не проверялась больше часа) и раз в 6 часов, без перезапуска. Когда выходит новый патч игры, в главном окне появляется баннер, а в трее — уведомление.
- Новая страница «Бой: урон и TTK»: выстрелы и время до убийства по зоне, бронежилету, шлему, дистанции и типу патронов (FMJ/HP/AP), урон за попадание, цена магазина, открытие патронов и совет, какие патроны брать. Модель совпадает с калькулятором MetaForge, включая падение урона с дистанцией и полёт пули.
- На плашке — выстрелы до убийства против выбранной брони и дистанции (грудь, голова, лучший патрон) и цена жизни: сборка с твоей бронёй и шлемом и сколько раз её можно купить на текущий баланс (красным — меньше трёх). Настраивается в главном окне.
- Страница «Экономика»: «Куда ушли деньги» — покупки по падению баланса в одном заходе к продавцу с привязкой к появившейся пушке или обвесам (иначе — патроны, броня или снаряжение) и суммы по видам; «Цель» — выбираешь предмет с ценой открытия и видишь, сколько не хватает, сколько играть при твоём среднем темпе и какой нужен уровень роли.
- В строке цены жизни на плашке — окупаемость: за сколько минут игры при твоём среднем заработке сборка отбивается.
- Исправлено пустое место под заголовками страниц в окне базы.
- Оценки времени убийства по всей программе (включая дробовики) считаются по одной модели брони: снижение по уровню, пробитие патрона и правила против брони.
- Раздел «Патч»: официальные записи разработчиков в Steam с прошлого патча, фильтр «Мои пушки» (пушки, которые узнавал оверлей, и обвесы к ним), удалённые предметы, номер сборки игры и все изменения предмета (раньше обрезалось до шести).

### 1.1.1 — 2026-11-07

- Меню распознаётся и в английской, и в русской версии игры. Русский язык в Windows больше не нужен: без него русские подписи узнаются по начертанию букв, как их читает английский OCR.
- Английская игра: кнопки «ATTC.» и «AMMO», заголовок «ATTACHMENTS» (или его обычное место, если не прочитался) и баланс вида «$12,500».

### 1.1.0 — 2026-11-06

- Проверка обновлений на GitHub при запуске и раз в 6 часов: баннер «Обновить / Что нового / Пропустить версию» и флаг «Обновлять автоматически» на главной. Скачанный файл сверяется с SHA256SUMS.txt; автоустановка ждёт, пока игра уйдёт в фон.
- Баланс стрельбища ($10 000) больше не считается заработком; скачок баланса — новая точка отсчёта, в темп заработка не входит.
- Баланс читается с учётом похожих на цифры букв и отдельного «$»; при ошибке — «не знаю» вместо суммы в 1000 раз меньше.
- Когда подсказка игры закрывает ячейки, плашка оставляет прежний результат; новый состав сборки показывается после двух одинаковых кадров.
- Снова работает «Запускать свёрнутым в трей».
- Без интернета программа не закрывается из-за недокачанных иконок или журнала патчей; скачанная база проверяется до замены, иконки, настройки и экономика пишутся атомарно.
- Исправлена утечка элементов на страницах «Патч» и «Сборка под бюджет».
- Урон дробовиков учитывает все дробины и штраф дроби по бронежилету.
- Боковой прицел больше не подменяет кратность основного; огромный бюджет не тормозит подбор.
- Пушка по картинке принимается при заметном отрыве или на двух кадрах подряд; совпадение по цене сверяется с картинкой.
- Значения настроек проверяются при загрузке; битые settings.json и economy.json откладываются в `.corrupt`.
- `--test --self-check` — проверки логики без игры.

### 1.0.0 — 2026-10-29

- Первый релиз Wardogs Overlay.
- Характеристики оружия и обвесов, варианты для слота и сравнение сборок.
- Подбор сборки по бюджету, база предметов, прогресс, экономика и журнал патча.
- Русский и английский интерфейс, горячие клавиши и настройки плашки.
- Исправлено DPI-масштабирование; увеличены шрифты и строки таблиц, исправлено обрезание подписей.
- Автоматическая сборка и публикация релизов через GitHub Actions.

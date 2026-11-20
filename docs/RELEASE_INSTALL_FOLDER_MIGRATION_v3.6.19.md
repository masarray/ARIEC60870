# ArSee v3.6.19 — legacy program-folder migration

Proposed release; not public until exact-source Windows CI and release gates are green.

The v3.6.18 branding release retained the old program directory on upgrade because `UsePreviousAppDir=yes` reused the registered directory. The new installer has `UsePreviousAppDir=no`, and for the **exact registered** `%LOCALAPPDATA%\\Programs\\ARIEC60870` installation it runs the existing Inno uninstaller before installing into `%LOCALAPPDATA%\\Programs\\ArSee`. This prevents two uninstall logs using one AppId in different directories. Existing custom installation directories are not silently orphaned.

The **distinct user workspace** `%LOCALAPPDATA%\\ARIEC60870` contains profile settings, signal mappings, captures and update journals. It is deliberately not renamed or removed by this change. It requires its own separately tested data migration to rename later.

The older in-app update worker verifies a fixed executable location. From an old install folder, the user is sent to the official Windows installer for this one-time relocation. Upgrades that are already in the canonical ArSee folder retain in-place updating.

Native Windows FAT must check authentic checksum-pinned v3.6.17 and v3.6.18 installs, collision failure before uninstall, migration to one ArSee.exe and shortcut, matching Inno registration, WPF smoke, uninstall and preservation of app data. No recursive deletion of unknown files is allowed.

**Limitation:** this migration uninstalls old program binaries before installing the replacement. If Windows loses power or setup fails during that narrow interval, reinstall the public setup. No user workspace deletion is performed, but this is not a transactionally reversible program-binary move. Do not claim fully proven until Windows CI passes.

**Published-updater compatibility:** The already-shipped v3.6.17/v3.6.18 update workers cannot safely relocate their own executable. Their distinct `/NOCLOSEAPPLICATIONS` and `/NOFORCECLOSEAPPLICATIONS` launch combination is rejected before uninstall when the registered install is still in the legacy folder. Windows CI includes this negative case. Users can still launch the official installer directly to migrate.

# ArSee v3.6.19 — legacy program-folder migration

**Public stable v3.6.19 — published 21 November 2026** from immutable source `b384e06b4ec435dae698ed995b02baf44499cefc`. [Exact-source Windows installer/updater and legacy FAT #44869011472](https://github.com/masarray/arsee/actions/runs/44869011472) and [release Package/provenance #44869528440](https://github.com/masarray/arsee/actions/runs/44869528440) passed.

The v3.6.18 branding release retained the old program directory on upgrade because `UsePreviousAppDir=yes` reused the registered directory. The new installer has `UsePreviousAppDir=no`, and for the **exact registered** `%LOCALAPPDATA%\\Programs\\ARIEC60870` installation it runs the existing Inno uninstaller before installing into `%LOCALAPPDATA%\\Programs\\ArSee`. This prevents two uninstall logs using one AppId in different directories. Existing custom installation directories are not silently orphaned.

The **distinct user workspace** `%LOCALAPPDATA%\\ARIEC60870` contains profile settings, signal mappings, captures and update journals. It is deliberately not renamed or removed by this change. It requires its own separately tested data migration to rename later.

The older in-app update worker verifies a fixed executable location. From an old install folder, the user is sent to the official Windows installer for this one-time relocation. Upgrades that are already in the canonical ArSee folder retain in-place updating.

Native Windows FAT tested genuine checksum-pinned previous public installers, including v3.6.17/v3.6.18 migration scenarios; collision fail-closed behavior, one ArSee.exe and shortcut, uninstall registration, WPF startup and preserved user-data evidence were checked on the exact release source. No recursive deletion of unknown files is allowed.

**Limitation:** this migration uninstalls old program binaries before installing the replacement. If Windows loses power or setup fails during that narrow interval, reinstall the public setup. No user workspace deletion is performed, but this is not a transactionally reversible program-binary move. Native Windows CI passed; do not claim power-loss rollback, arbitrary damaged installations or universal device interoperability have been proven.

**Published-updater compatibility:** The already-shipped v3.6.17/v3.6.18 update workers cannot safely relocate their own executable. Their distinct `/NOCLOSEAPPLICATIONS` and `/NOFORCECLOSEAPPLICATIONS` launch combination is rejected before uninstall when the registered install is still in the legacy folder. Windows CI includes this negative case. Users can still launch the official installer directly to migrate.

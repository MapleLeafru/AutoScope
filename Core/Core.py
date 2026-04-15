import sqlite3


class Core:

    def save(self, data, db_path):

        # если список — обрабатываем по одному
        if isinstance(data, list):
            for item in data:
                self._save_one(item, db_path)
            return

        self._save_one(data, db_path)


    # =========================================================
    # SINGLE SAVE
    # =========================================================

    def _save_one(self, data, db_path):

        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()

        # 1. ads
        ads_id = self._get_or_create_ads(cursor, data)

        # 2. последний snapshot
        last_snapshot = self._get_last_snapshot(cursor, ads_id)

        # 3. сравнение
        has_changes = self._has_changes(last_snapshot, data)

        # 4. check
        check_id = self._create_check(cursor, ads_id, has_changes)

        # 5. snapshot
        if has_changes:
            self._create_snapshot(cursor, check_id, data)

        conn.commit()
        conn.close()

        print(f"[CORE] ads_id={ads_id}, check_id={check_id}, has_changes={has_changes}")


    # =========================================================
    # ADS
    # =========================================================

    def _get_or_create_ads(self, cursor, data):
        url = data.get("url")

        cursor.execute("SELECT id FROM ads WHERE url = ?", (url,))
        row = cursor.fetchone()

        if row:
            return row[0]

        cursor.execute(
            "INSERT INTO ads (source, url) VALUES (?, ?)",
            (
                data.get("source"),
                url
            )
        )

        return cursor.lastrowid


    # =========================================================
    # LAST SNAPSHOT
    # =========================================================

    def _get_last_snapshot(self, cursor, ads_id):
        cursor.execute(
            """
            SELECT s.*
            FROM ads_snapshots s
            JOIN ads_checks c ON s.check_id = c.id
            WHERE c.ads_id = ?
            ORDER BY c.id DESC
            LIMIT 1
            """,
            (ads_id,)
        )

        row = cursor.fetchone()

        if not row:
            return None

        columns = [col[0] for col in cursor.description]
        return dict(zip(columns, row))


    # =========================================================
    # COMPARE
    # =========================================================

    def _has_changes(self, last_snapshot, new_data):

        if last_snapshot is None:
            return True

        ignored_fields = {"id", "check_id"}

        for key, value in new_data.items():
            if key in ignored_fields:
                continue

            old_value = last_snapshot.get(key)

            if old_value != value:
                return True

        return False


    # =========================================================
    # ADS_CHECKS
    # =========================================================

    def _create_check(self, cursor, ads_id, has_changes):
        cursor.execute(
            "INSERT INTO ads_checks (ads_id, has_changes) VALUES (?, ?)",
            (ads_id, int(has_changes))
        )

        return cursor.lastrowid


    # =========================================================
    # ADS_SNAPSHOTS
    # =========================================================

    def _create_snapshot(self, cursor, check_id, data):

        cursor.execute(
            """
            INSERT INTO ads_snapshots (
                check_id,
                brand, model, price, year,
                production_country, sale_region,
                license_plate, mileage,
                transmission, drive_type,
                color, body_type,
                steering_wheel,
                engine_power, engine_volume,
                engine_model, fuel_type,
                description
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                check_id,
                data.get("brand"),
                data.get("model"),
                data.get("price"),
                data.get("year"),
                data.get("production_country"),
                data.get("sale_region"),
                data.get("license_plate"),
                data.get("mileage"),
                data.get("transmission"),
                data.get("drive_type"),
                data.get("color"),
                data.get("body_type"),
                data.get("steering_wheel"),
                data.get("engine_power"),
                data.get("engine_volume"),
                data.get("engine_model"),
                data.get("fuel_type"),
                data.get("description")
            )
        )
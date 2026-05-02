import sqlite3
import sys

class Core:

    def __init__(self, db_config=None):
        self.db_config = db_config or {}

    def save(self, data, db_path):

        # если список — обрабатываем по одному
#        if isinstance(data, list):
#            for item in data:
#                self._save_one(item, db_path)
#            return
#
#        self._save_one(data, db_path)

# ---------------------------------------------------------------- аналог print(f"[CORE] ads_id={ads_id}, check_id={check_id}, has_changes={has_changes}")
        results = []
        if isinstance(data, list):
            for item in data:
                results.append(self._save_one(item, db_path))
        else:
            results.append(self._save_one(data, db_path))

        return results
# ----------------------------------------------------------------


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

        # print(f"[CORE] ads_id={ads_id}, check_id={check_id}, has_changes={has_changes}")
        return {
            "ads_id": ads_id,
            "check_id": check_id,
            "has_changes": has_changes
        }

    # =========================================================
    # ADS
    # =========================================================

    def _get_or_create_ads(self, cursor, data):
        url = data.get("url")

        cursor.execute("SELECT id FROM ads WHERE url = ?", (url,))
        row = cursor.fetchone()

        if row:
            return row[0]

        if "url" not in data:
            raise Exception("Missing required field: url")  # !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!! Хардкот на обязательне поля

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

        for key in last_snapshot.keys():

            if key in ignored_fields:
                continue

            old_value = last_snapshot.get(key)
            new_value = new_data.get(key)

            if old_value != new_value:
#                print(f"[DIFF] {key}: {old_value} != {new_value}", file=sys.stderr)                         # debug
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

        snapshot_fields = list(
            self.db_config.get("ads_snapshots", {}).keys()
        )

        columns = ["check_id"] + snapshot_fields

        values = [check_id] + [data.get(field) for field in snapshot_fields]

        placeholders = ", ".join(["?"] * len(columns))
        columns_sql = ", ".join(columns)

        query = f"""
            INSERT INTO ads_snapshots ({columns_sql})
            VALUES ({placeholders})
        """

        if not snapshot_fields:
            raise Exception("ads_snapshots config is empty")

        cursor.execute(query, values)

    # =========================================================
    # ADS_SNAPSHOTS
    # =========================================================

    def fetch_all(self, db_path):
        conn = sqlite3.connect(db_path)
        conn.row_factory = sqlite3.Row
        cursor = conn.cursor()

        query = """
            SELECT a.url, a.source, s.*
            FROM ads a
            JOIN ads_checks c ON a.id = c.ads_id
            JOIN ads_snapshots s ON s.check_id = c.id
            WHERE c.id = (
                SELECT MAX(c2.id)
                FROM ads_checks c2
                WHERE c2.ads_id = a.id
            )
        """

        cursor.execute(query)
        rows = cursor.fetchall()

        conn.close()

        return [dict(row) for row in rows]
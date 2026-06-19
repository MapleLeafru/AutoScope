# -*- coding: utf-8 -*-
import sqlite3


class Core:
    def __init__(self, db_config=None):
        # Хранит конфиг БД, по которому Core понимает структуру таблиц и snapshot-полей.
        self.db_config = db_config or {}

    def save(self, data, db_path):
        # Сохраняет один объект или список объектов и возвращает результаты записи.
        results = []

        if isinstance(data, list):
            for item in data:
                results.append(self._save_one(item, db_path))
        else:
            results.append(self._save_one(data, db_path))

        return results

    def _save_one(self, data, db_path):
        # Сохраняет одно объявление: ads -> ads_checks -> ads_snapshots при изменениях.
        conn = sqlite3.connect(db_path)

        try:
            cursor = conn.cursor()

            ads_id = self._get_or_create_ads(cursor, data)
            last_snapshot = self._get_last_snapshot(cursor, ads_id)
            has_changes = self._has_changes(last_snapshot, data)
            check_id = self._create_check(cursor, ads_id, has_changes)

            if has_changes:
                self._create_snapshot(cursor, check_id, data)

            conn.commit()

            return {
                "ads_id": ads_id,
                "check_id": check_id,
                "has_changes": has_changes,
            }

        finally:
            conn.close()

    def _get_or_create_ads(self, cursor, data):
        # Возвращает существующее объявление по url или создаёт новую запись в ads.
        url = data.get("url")

        if not url:
            raise RuntimeError("Missing required field: url")

        cursor.execute("SELECT id FROM ads WHERE url = ?", (url,))
        row = cursor.fetchone()

        if row:
            return row[0]

        cursor.execute(
            "INSERT INTO ads (source, url) VALUES (?, ?)",
            (
                data.get("source"),
                url,
            ),
        )

        return cursor.lastrowid

    def _get_last_snapshot(self, cursor, ads_id):
        # Получает последний сохранённый snapshot объявления.
        cursor.execute(
            """
            SELECT s.*
            FROM ads_snapshots s
            JOIN ads_checks c ON s.check_id = c.id
            WHERE c.ads_id = ?
            ORDER BY c.id DESC
            LIMIT 1
            """,
            (ads_id,),
        )

        row = cursor.fetchone()

        if not row:
            return None

        columns = [col[0] for col in cursor.description]
        return dict(zip(columns, row))

    def _has_changes(self, last_snapshot, new_data):
        # Сравнивает новый объект с последним snapshot и определяет, нужно ли сохранять новый снимок.
        if last_snapshot is None:
            return True

        ignored_fields = {"id", "check_id"}

        for key in last_snapshot.keys():
            if key in ignored_fields:
                continue

            old_value = last_snapshot.get(key)
            new_value = new_data.get(key)

            if old_value != new_value:
                return True

        return False

    def _create_check(self, cursor, ads_id, has_changes):
        # Создаёт запись проверки объявления в ads_checks.
        cursor.execute(
            "INSERT INTO ads_checks (ads_id, has_changes) VALUES (?, ?)",
            (ads_id, int(has_changes)),
        )

        return cursor.lastrowid

    def _create_snapshot(self, cursor, check_id, data):
        # Создаёт snapshot объявления на основе полей из конфига ads_snapshots.
        snapshot_fields = list(self.db_config.get("ads_snapshots", {}).keys())

        if not snapshot_fields:
            raise RuntimeError("ads_snapshots config is empty")

        columns = ["check_id"] + snapshot_fields
        values = [check_id] + [data.get(field) for field in snapshot_fields]

        placeholders = ", ".join(["?"] * len(columns))
        columns_sql = ", ".join(columns)

        query = f"""
            INSERT INTO ads_snapshots ({columns_sql})
            VALUES ({placeholders})
        """

        cursor.execute(query, values)

    def fetch_all(self, db_path):
        # Возвращает последние snapshot-записи по всем объявлениям для обратной совместимости.
        return self.fetch_for_output(
            db_path,
            {
                "latest_only": True,
                "only_changed": False,
                "filters": {},
            },
        )

    def fetch_for_output(self, db_path, query=None):
        # Возвращает snapshot-записи для OutputPipeline с учётом фильтров анализа.
        query = query or {}
        latest_only = query.get("latest_only", True)
        only_changed = query.get("only_changed", False)
        filters = query.get("filters", {}) or {}

        conn = sqlite3.connect(db_path)
        conn.row_factory = sqlite3.Row

        try:
            cursor = conn.cursor()
            sql, params = self._build_output_query(latest_only, only_changed, filters)
            cursor.execute(sql, params)

            rows = cursor.fetchall()
            return [dict(row) for row in rows]

        finally:
            conn.close()

    def _build_output_query(self, latest_only, only_changed, filters):
        # Собирает SQL-запрос для выборки snapshot-записей с фильтрами.
        snapshot_fields = self._get_snapshot_select_fields()

        sql = f"""
            SELECT
                a.id AS ads_id,
                a.url AS url,
                a.source AS source,
                c.id AS check_id,
                c.checked_at AS checked_at,
                c.has_changes AS has_changes,
                s.id AS snapshot_id,
                {snapshot_fields}
            FROM ads a
            JOIN ads_checks c ON a.id = c.ads_id
            JOIN ads_snapshots s ON s.check_id = c.id
        """

        where_parts = []
        params = []

        if latest_only:
            where_parts.append(
                """
                s.id = (
                    SELECT s2.id
                    FROM ads_snapshots s2
                    JOIN ads_checks c2 ON s2.check_id = c2.id
                    WHERE c2.ads_id = a.id
                    ORDER BY c2.id DESC
                    LIMIT 1
                )
                """
            )

        if only_changed:
            where_parts.append("c.has_changes = 1")

        self._add_text_filter(where_parts, params, filters, "brand", "s.brand", exact=True)
        self._add_text_filter(where_parts, params, filters, "model", "s.model", exact=False)
        self._add_text_filter(where_parts, params, filters, "sale_region", "s.sale_region", exact=False)
        self._add_year_filters(where_parts, params, filters)

        if where_parts:
            sql += "\nWHERE " + "\n  AND ".join(where_parts)

        sql += "\nORDER BY c.checked_at DESC, c.id DESC"

        return sql, params

    def _get_snapshot_select_fields(self):
        # Возвращает список snapshot-полей из конфига БД для SELECT.
        snapshot_fields = list(self.db_config.get("ads_snapshots", {}).keys())

        if not snapshot_fields:
            snapshot_fields = [
                "brand",
                "model",
                "price",
                "year",
                "brand_origin_country",
                "sale_region",
                "license_plate",
                "mileage",
                "transmission",
                "drive_type",
                "color",
                "body_type",
                "steering_wheel",
                "engine_power",
                "engine_volume",
                "engine_model",
                "fuel_type",
                "octane",
                "powertrain_type",
                "description",
            ]

        return ",\n                ".join([f"s.{field} AS {field}" for field in snapshot_fields])

    def _add_text_filter(self, where_parts, params, filters, filter_name, column_name, exact=False):
        # Добавляет текстовый фильтр в SQL, если пользователь указал значение.
        value = filters.get(filter_name)
        if not value:
            return

        if exact:
            where_parts.append(f"LOWER({column_name}) = LOWER(?)")
            params.append(value)
        else:
            where_parts.append(f"LOWER({column_name}) LIKE LOWER(?)")
            params.append(f"%{value}%")

    def _add_year_filters(self, where_parts, params, filters):
        # Добавляет фильтр диапазона года выпуска.
        year_from = filters.get("year_from")
        year_to = filters.get("year_to")

        if year_from is not None:
            where_parts.append("s.year >= ?")
            params.append(year_from)

        if year_to is not None:
            where_parts.append("s.year <= ?")
            params.append(year_to)

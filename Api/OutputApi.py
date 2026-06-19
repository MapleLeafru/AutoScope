# -*- coding: utf-8 -*-


class OutputApi:
    def __init__(self, db_config=None):
        # Хранит конфиг БД для подготовки выборок и фильтров анализа.
        self.db_config = db_config or {}

    def prepare(self, request):
        # Готовит параметры выборки для Core на основе outputSettings из запроса.
        settings = request.get("outputSettings", {}) or {}

        return {
            "type": "snapshots",
            "latest_only": self._get_bool(settings, "latestOnly", True),
            "only_changed": self._get_bool(settings, "onlyChanged", False),
            "filters": {
                "brand": self._clean_string(settings.get("brand")),
                "model": self._clean_string(settings.get("model")),
                "sale_region": self._clean_string(settings.get("saleRegion")),
                "year_from": self._get_int_or_none(settings.get("yearFrom")),
                "year_to": self._get_int_or_none(settings.get("yearTo")),
            },
        }

    def process(self, data, query=None):
        # Подготавливает данные перед передачей во внешний анализатор.
        prepared_data = data if isinstance(data, list) else []
        query = query or {}

        return {
            "data": prepared_data,
            "meta": {
                "count": len(prepared_data),
                "latestOnly": query.get("latest_only", True),
                "onlyChanged": query.get("only_changed", False),
                "filters": query.get("filters", {}),
            },
        }

    def _get_bool(self, settings, key, default):
        # Безопасно читает bool-настройку из словаря.
        value = settings.get(key, default)

        if isinstance(value, bool):
            return value

        if isinstance(value, str):
            return value.strip().lower() in ["true", "1", "yes", "y", "да"]

        return bool(value)

    def _get_int_or_none(self, value):
        # Преобразует значение в int или возвращает None.
        if value is None:
            return None

        if isinstance(value, int):
            return value

        if isinstance(value, str):
            value = value.strip()
            if not value:
                return None

            try:
                return int(value)
            except ValueError:
                return None

        return None

    def _clean_string(self, value):
        # Очищает строковый фильтр. Пустые строки превращает в None.
        if not isinstance(value, str):
            return None

        value = value.strip()
        return value if value else None

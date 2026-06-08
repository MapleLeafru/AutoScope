# -*- coding: utf-8 -*-


class InputApi:
    # Резервный список полей на случай, если конфиг БД не загрузился или пустой.
    FALLBACK_ALLOWED_FIELDS = {
        "source",
        "url",
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
    }

    # Резервный список обязательных полей.
    FALLBACK_REQUIRED_FIELDS = {"url"}

    # Соответствие имён полей между внешними парсерами и внутренней схемой AutoScope.
    FIELD_MAPPING = {
        "powertrain": "powertrain_type",
    }

    def __init__(self, db_config, brand_country_map):
        # Хранит конфиг БД и справочники, нужные для нормализации данных.
        self.db_config = db_config or {}
        self.brand_country_map = brand_country_map or {}

        self.required_fields = set(
            self.db_config.get("required_fields", self.FALLBACK_REQUIRED_FIELDS)
        )
        self.allowed_fields = self._build_allowed_fields()

    def process(self, data):
        # Нормализует один объект или список объектов и возвращает данные с meta-информацией.
        if data is None:
            return {
                "data": None,
                "meta": {"skipped": 0},
            }

        if isinstance(data, list):
            return self._process_list(data)

        normalized = self._normalize(data)
        skipped = 1 if normalized is None else 0

        return {
            "data": normalized,
            "meta": {"skipped": skipped},
        }

    def _process_list(self, items):
        # Обрабатывает список объектов и считает, сколько записей было пропущено.
        result = []
        skipped = 0

        for item in items:
            normalized = self._normalize(item)

            if normalized is None:
                skipped += 1
                continue

            result.append(normalized)

        return {
            "data": result,
            "meta": {"skipped": skipped},
        }

    def _normalize(self, data):
        # Приводит одну запись парсера к внутреннему формату AutoScope.
        if not isinstance(data, dict):
            return None

        result = {}

        for raw_key, value in data.items():
            key = self.FIELD_MAPPING.get(raw_key, raw_key)

            if key not in self.allowed_fields:
                continue

            value = self._normalize_value(key, value)
            result[key] = value

        self._enrich_brand_country(result)

        if not self._has_required_fields(result):
            return None

        return result

    def _normalize_value(self, key, value):
        # Очищает строковые значения и приводит простые числа к int/float.
        if value == "":
            return None

        if isinstance(value, str):
            value = value.strip()

            if key in ["brand", "model", "color", "body_type"]:
                value = value.title()

        return self._try_cast_number(value)

    def _enrich_brand_country(self, data):
        # Подставляет страну происхождения бренда, если поле не пришло от парсера.
        if data.get("brand_origin_country"):
            return

        brand = data.get("brand")
        if not brand:
            return

        country = self._get_brand_country(brand)
        if country:
            data["brand_origin_country"] = country

    def _get_brand_country(self, brand):
        # Ищет бренд в справочнике с несколькими вариантами регистра.
        if not brand:
            return None

        brand_clean = brand.strip()
        variants = [
            brand_clean,
            brand_clean.title(),
            brand_clean.upper(),
            brand_clean.lower(),
        ]

        for variant in variants:
            if variant in self.brand_country_map:
                return self.brand_country_map[variant]

        return None

    def _has_required_fields(self, data):
        # Проверяет наличие обязательных полей после нормализации.
        for field in self.required_fields:
            if not data.get(field):
                return False

        return True

    def _build_allowed_fields(self):
        # Собирает разрешённые поля из конфига БД, а при его отсутствии использует fallback.
        allowed_fields = set()

        allowed_fields.update(self.db_config.get("ads", {}).keys())
        allowed_fields.update(self.db_config.get("ads_snapshots", {}).keys())

        if not allowed_fields:
            return set(self.FALLBACK_ALLOWED_FIELDS)

        return allowed_fields

    def _try_cast_number(self, value):
        # Преобразует строковые числа в int или float, остальные значения оставляет без изменений.
        if not isinstance(value, str):
            return value

        if value.isdigit():
            return int(value)

        try:
            return float(value)
        except ValueError:
            return value

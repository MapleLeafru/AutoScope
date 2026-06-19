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

    def __init__(self, db_config, brand_country_map=None, api_settings=None, dictionaries=None):
        # Хранит конфиг БД, справочники и настройки обработки данных.
        self.db_config = db_config or {}
        self.brand_country_map = brand_country_map or {}
        self.api_settings = api_settings or {}
        self.dictionaries = dictionaries or {}

        self.transmission_map = self.dictionaries.get("transmission", {})
        self.drive_type_map = self.dictionaries.get("drive_type", {})
        self.fuel_type_map = self.dictionaries.get("fuel_type", {})

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

        if self._is_enabled("brandCountryEnrichment", True):
            self._enrich_brand_country(result)

        if self._is_enabled("transmissionNormalization", True):
            self._normalize_by_dictionary(result, "transmission", self.transmission_map)

        if self._is_enabled("driveTypeNormalization", True):
            self._normalize_by_dictionary(result, "drive_type", self.drive_type_map)

        if self._is_enabled("fuelTypeNormalization", True):
            self._normalize_by_dictionary(result, "fuel_type", self.fuel_type_map)

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

    def _is_enabled(self, setting_name, default=True):
        # Проверяет, включена ли конкретная настройка обработки данных.
        value = self.api_settings.get(setting_name, default)

        if isinstance(value, bool):
            return value

        if isinstance(value, str):
            return value.strip().lower() in ["true", "1", "yes", "y", "да"]

        return bool(value)

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

    def _normalize_by_dictionary(self, data, field_name, dictionary):
        # Нормализует поле по справочнику, если значение найдено.
        if not dictionary:
            return

        value = data.get(field_name)
        if value is None:
            return

        normalized = self._get_from_dictionary(dictionary, value)
        if normalized is not None:
            data[field_name] = normalized

    def _get_brand_country(self, brand):
        # Ищет бренд в справочнике с несколькими вариантами регистра.
        return self._get_from_dictionary(self.brand_country_map, brand)

    def _get_from_dictionary(self, dictionary, value):
        # Ищет значение в справочнике с учётом регистра и лишних пробелов.
        if value is None:
            return None

        value_clean = str(value).strip()
        if not value_clean:
            return None

        variants = [
            value_clean,
            value_clean.title(),
            value_clean.upper(),
            value_clean.lower(),
        ]

        for variant in variants:
            if variant in dictionary:
                return dictionary[variant]

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

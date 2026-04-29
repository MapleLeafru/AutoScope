import json
import os


class InputApi:

    # =========================================================
    # ALLOWED FIELDS (контракт с БД)
    # =========================================================

    ALLOWED_FIELDS = { # !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!! Надо блять сделать подтягивание конфига бд из json !!!!!!!!!!!!!!!!! Сделал, но удалять нельзя, это на случай fallback, но теперь не зависит
        # ads
        "source",
        "url",

        # ads_snapshots
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
        "description"
    }

    REQUIRED_FIELDS = { # !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!! Надо блять сделать подтягивание конфига бд из json !!!!!!!!!!!!!!!!! Сделал, но удалять нельзя, это на случай fallback, но теперь не зависит
        "url"
    }

    FIELD_MAPPING = {
    "powertrain": "powertrain_type"
}

    # =========================================================
    # INIT
    # =========================================================

    def __init__(self, full_config=None):
        full_config = full_config or {}

        self.config = full_config
        self.db_config = full_config.get("db", {})

        self.brand_country_map = full_config.get("brand_country_map", {})
#        self.brand_country_map = self.config.get("brand_country_map", {}) # !!!!!!!!!!!!!!!!!!!!!!!!!!!! Потом добавить подгрузку конфига серез конфиг лоадер

#        self._load_brand_country_map()

        self.required_fields = set(self.db_config.get("required_fields", ["url"]))

        # =========================================================
        # DYNAMIC CONFIG FROM DB CONFIG
        # =========================================================

        #snapshots = self.config.get("ads_snapshots", {})
        #self.allowed_fields_dynamic = set(snapshots.keys())

        self.db_fields = set(self.db_config.get("ads_snapshots", {}).keys())

        # =========================================================
        # DYNAMIC ALLOWED FIELDS (из DB конфигурации)
        # =========================================================

        self.allowed_fields_dynamic = set(self.db_config.get("ads_snapshots", {}).keys())

        # добавляем поля из таблицы ads (они не в snapshots)
        self.allowed_fields_dynamic.update(
            self.db_config.get("ads", {}).keys()
        )

#        self.allowed_fields_dynamic = {
#            "source", "url",
#            "brand", "model", "price", "year",
#            "mileage", "transmission", "drive_type",
#            "color", "body_type", "steering_wheel",
#            "engine_power", "engine_volume",
#            "fuel_type", "octane", "powertrain_type",
#            "description", "sale_region"
#        }

        self.required_fields = set(
            self.config.get("required_fields", ["url"])
        )

        # fallback safety
#        if not self.allowed_fields_dynamic:
#            self.allowed_fields_dynamic = {
#                "source", "url",
#                "brand", "model", "price", "year"
#            }

#    def __init__(self, config_path=None):
#        self.config = {}
#        self.brand_country_map = {}
#        self.config_path = config_path

        ## if config_path:
        ##     self._load_config(config_path)

        # загрузка BrandCountryMap.json
#        self._load_brand_country_map()
        # print("Brand map size:", len(self.brand_country_map))                                                                 # debag


    # =========================================================
    # CONFIG
    # =========================================================

    ## def _load_config(self, config_path):
    ##     with open(config_path, "r", encoding="utf-8") as f:
    ##         self.config = json.load(f)


    # =========================================================
    # BRAND COUNTRY MAP
    # =========================================================

#    def _load_brand_country_map(self):
#        try:
#            self.brand_country_map = self.config.get("brand_country_map", {})
#        except Exception as e:
#            print(f"ERROR load BrandCountryMap: {e}")
#            self.brand_country_map = {}

#        try:
#            path = os.path.join(self.config_path, "BrandCountryMap.json")
#
#            with open(path, "r", encoding="utf-8") as f:
#                self.brand_country_map = json.load(f)
#
#        except Exception as e:
#            print("ERROR load BrandCountryMap:", str(e))
#            self.brand_country_map = {}

#    def _load_brand_country_map(self):
#        try:
#        #with open("../../../../Configs/BrandCountryMap.json", "r", encoding="utf-8") as f:                                          # Временное решение пока корень не передаётся
#            with open("C:/Users/MaplLeaf/source/repos/AutoScope/Configs/BrandCountryMap.json", "r", encoding="utf-8") as f:                                          # СУПЕР Временное решение пока корень не передаётся !!!!!!!!!!!!!!!!!!!!!
#                self.brand_country_map = json.load(f)
#        except:
#            print ("ERROR load BrandCountryMap")                                                                                                                # debag
#            self.brand_country_map = {}


    # =========================================================
    # MAIN
    # =========================================================

    # Новый тестовый process с логгированием
    def process(self, data):
        if data is None:
            return {
                "data": None,
                "meta": {
                    "skipped": 0
                }
            }

        skipped = 0

        # список объектов
        if isinstance(data, list):
            result = []

            for item in data:
                normalized = self._normalize(item)

                if normalized is None:
                    skipped += 1
                    continue

                result.append(normalized)

            return {
                "data": result,
                "meta": {
                    "skipped": skipped
                }
            }

        # один объект
        normalized = self._normalize(data)

        if normalized is None:
            skipped = 1

        return {
            "data": normalized,
            "meta": {
                "skipped": skipped
            }
        }

#    def process(self, data):
#        if data is None:
#            return None
#
#        # список объектов
#        if isinstance(data, list):
#            result = []
#
#            for item in data:
#                normalized = self._normalize(item)
#
#                if normalized is None:
#                    # логируем если есть logger
##                    logger = getattr(self, "logger", None)
##                    if logger:
##                        logger.warning("API", f"Skipped invalid item: {item}") # !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!! Добавить логгирование пустых полей
#                    continue
#
#                result.append(normalized)
#
#            ## result = self._apply_config(result)
#            return result
#
#        # один объект
#        normalized = self._normalize(data)
#
#        ## normalized = self._apply_config(normalized)
#        return normalized


    # =========================================================
    # NORMALIZE
    # =========================================================

    def _normalize(self, data):

        if not isinstance(data, dict):
            return None

        result = {}

        for key, value in data.items():

            # маппинг полей (parser → API)
            original_key = key
            mapped_key = self.FIELD_MAPPING.get(key, key)

            # фильтрация полей
#            if key not in self.ALLOWED_FIELDS:
#                continue
            raw_key = key
            key = self.FIELD_MAPPING.get(key, key)
            if key not in self.allowed_fields_dynamic:
                continue

            # пустые строки → None
            if value == "":
                value = None

            # чистка строк
            if isinstance(value, str):
                value = value.strip()

                # нормализация регистра
                if key in ["brand", "model", "color", "body_type"]:
                    value = value.title()

            # приведение чисел
            value = self._try_cast_number(value)

            result[key] = value

        # =========================================================
        # ENRICHMENT: BRAND COUNTRY
        # =========================================================

        # если страна не пришла — пробуем определить
        if not result.get("brand_origin_country"):
            brand = result.get("brand")

            if brand:
                country = self._get_brand_country(brand)

                if country:
                    result["brand_origin_country"] = country

        # обязательные поля
#        for field in self.REQUIRED_FIELDS:
        for field in self.required_fields:
            if not result.get(field):
                return None

        return result


    # =========================================================
    # BRAND NORMALIZATION
    # =========================================================

    def _get_brand_country(self, brand):

        if not brand:
            return None

        # варианты нормализации
        brand_clean = brand.strip()

        variants = [
            brand_clean,
            brand_clean.title(),
            brand_clean.upper(),
            brand_clean.lower()
        ]

        for v in variants:
            if v in self.brand_country_map:
                return self.brand_country_map[v]

        return None


    # =========================================================
    # CAST
    # =========================================================

    def _try_cast_number(self, value):

        if isinstance(value, str):

            # int
            if value.isdigit():
                return int(value)

            # float
            try:
                return float(value)
            except:
                pass

        return value


    # =========================================================
    # CONFIG APPLY (будущее)
    # =========================================================

    ## def _apply_config(self, data):
    ##     if not self.config:
    ##         return data

    ##     allowed_fields = set(self.config.get("ads_snapshots", {}).keys())

    ##     def filter_item(item):
    ##         result = {}
    ##         for key, value in item.items():
    ##             if key in allowed_fields or key in ("url", "source"):
    ##                 result[key] = value
    ##         return result

    ##     if isinstance(data, list):
    ##         return [filter_item(item) for item in data]

    ##     return filter_item(data)
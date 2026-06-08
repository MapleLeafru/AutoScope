# -*- coding: utf-8 -*-


class OutputApi:
    def __init__(self, db_config=None):
        # Хранит конфиг БД для будущей подготовки выборок и фильтров анализа.
        self.db_config = db_config or {}

    def prepare(self, request):
        # Готовит параметры выборки для Core. Сейчас возвращает базовый режим "все записи".
        return {
            "type": "all"
        }

    def process(self, data):
        # Подготавливает данные перед передачей во внешний анализатор.
        if not data:
            return {
                "data": [],
                "meta": {"count": 0},
            }

        return {
            "data": data,
            "meta": {"count": len(data)},
        }

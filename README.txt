Таски:
1.
2.
3.
4.
5.

Ошибка NETSDK1004 файл ресурсов "...\obj\project.assets.json" не найден. Восстановите пакет NuGet, чтобы создать его.
dotnet restore
python -m compileall AutoScope\Utils - компилирует файлы питона, после того как скачал на новую машину

import sys
!{sys.executable} -m pip install -r "../requirements.txt"
!{sys.executable} -m pip freeze > "../requirements.lock.txt"

На проде удалить ненужные пакеты: pip, setuptools wheel (если ты не будем ставить пакеты на проде) для того что бы уменьшить размер приложения, подробнее у gpt
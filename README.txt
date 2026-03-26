import sys
!{sys.executable} -m pip install -r "../requirements.txt"
!{sys.executable} -m pip freeze > "../requirements.lock.txt"
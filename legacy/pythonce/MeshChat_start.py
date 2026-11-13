# MeshChat_start.py - tap THIS file to start MeshChat.
#
# It loads meshchat_web.py as a module, so Python saves a ready-made
# copy (meshchat_web.pyc) on the card. From the second launch on, Python
# skips re-reading the whole program and MeshChat starts faster.
#
# Keep this file in the same folder as meshchat_web.py.
# Edit settings in meshchat_web.py as before - the saved copy is
# rebuilt automatically whenever that file changes.

import sys, os, time

started = time.time()
try:
    here = os.path.dirname(__file__)
except NameError:
    here = ''
if not here and sys.path:
    here = sys.path[0]
if here and here not in sys.path:
    sys.path.insert(0, here)

import meshchat_web
sys.stdout.write("-- program loaded in %.1f s\n" % (time.time() - started))
meshchat_web.main()

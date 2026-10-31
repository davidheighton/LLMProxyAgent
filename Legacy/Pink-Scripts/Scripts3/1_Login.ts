# TWX Script Pack 1: TWGS Login Script
# Author           : Xide
# Description      : A login script for standard TWGS systems (tested for
#                    version 1.01.39).  Will enter in a user name, log into
#                    the game, passing all the messages, and deposit the user
#                    at the game command prompt.  To use, you will need to
#                    set the appropriate program variables:
#
#                    LOGINNAME - Your login name (to the TWGS system)
#                    PASSWORD - The password to your game account
#                    GAME - The letter of the game on the system
#
# Trigger Point    : On server connection.
# Warnings         : None.
# Other            : Useful to put this script in the 'Connection accepted'
#                    program event if you need to log in quickly.

# terminate script if disconnected
setEventTrigger 0 :End "Connection lost"

waitfor "(ENTER for none):"
send LoginName "*"
waitfor "Trade Wars 2002 Game Server"
send Game
waitfor "module now loading."
send "*t***" Password "*"
waitfor "Password?"
setTextLineTrigger 1 :End "Sector  : "
setTextTrigger 2 :Pause "[Pause]"
setTextTrigger 3 :Pause "Do you wish to clear some avoids?"
pause

:Pause
send "*"
killTrigger 2
setTextTrigger 2 :Pause "[Pause]"
pause

:End

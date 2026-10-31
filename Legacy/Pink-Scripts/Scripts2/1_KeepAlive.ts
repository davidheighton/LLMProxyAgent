# TWX Script Pack 1: Keepalive script
# Author           : Xide
# Description      : A VERY simple script which sends a "#" every 60
#                    seconds to keep the connection alive.
# Trigger Point    : Anywhere within TW where the global functions are
#                    working.
# Warnings         : Will send the "#" character every 60 sectors, wether
#                    the program is connected or not.  This character can
#                    sometimes disrupt things such as login and password
#                    prompts, etc.  I advise you only use it when your
#                    not at the console doing things.

:wait
send "#"
setDelayTrigger delay :wait 60000
pause

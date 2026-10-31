#Chat.TS
#Author: Grim Reaper aka BETA
#Comments: Shrug, I needed something to let me talk.
#
#Desc: Chat Script. I can make one with auto replies if needed.
#Run during SSM or WT, colonizing, ect.. 
#
#Directions: YOU MUST type in either ` or ' BEFORE your text.
#
#
#
#Chatbotscript, just helps you talk while scripting

ECHO ANSI_3 "**" ansi_12 "////////" ansi_3 "Chat Version " ansi_10 ".9B" ansi_12 "\\\\\\\\*"
ECHO ANSI_5  ansi_12 "////////" ansi_5 "Created by Grim Reaper aka " ansi_12 "BETA" ansi_12 "\\\\\\\\*"
Echo ansi_5  ansi_12 "////////" ansi_5 " Make sure you type ` or '" Ansi_12 " BEFORE" ansi_5 " your text" ansi_12 "\\\\\\\\"

:top

ECHO ANSI_12 "*Enter Text here: "

getconsoleinput $chat

send $chat "*"

goto :top
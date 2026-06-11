echo ======================================================
echo  Analyst capture: TIME / WEATHER / SKY
echo  Verbatim time/weather/sky strings for the Sky widget.
echo  Preconditions are shown in the confirmation dialog.
echo  5 verbs with 3s gaps (never trips the 2-command
echo  type-ahead throttle), then ~90s for ambient lines.
echo ======================================================
pause 2

echo --- [1/6] TIME : Elanthian date + time of day ---
put time
pause 3

echo --- [2/6] WEATHER : current conditions ---
put weather
pause 3

echo --- [3/6] SKY : sun, moons, cloud cover (all-character) ---
put sky
pause 3

echo --- [4/6] PERCEIVE : Moon Mage celestial read (optional) ---
put perceive
pause 3

echo --- [5/6] PREDICT : Moon Mage divination forecast (optional) ---
put predict
pause 3

echo --- [6/6] AMBIENT : standing still 90s for the atmospherics stream ---
echo (Do NOT move or type. Capturing passive weather/time transition lines.)
pause 30
echo ...30s elapsed...
pause 30
echo ...60s elapsed...
pause 30
echo --- CAPTURE COMPLETE. ---

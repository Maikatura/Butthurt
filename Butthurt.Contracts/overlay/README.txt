==============================================================================
  BUTTHURT STREAM OVERLAY
==============================================================================

  !!  WARNING  !!
  Changing anything in this folder can break the overlay.
  Edit at your own risk, and keep a copy of the files before you start.

------------------------------------------------------------------------------

This folder is the page your OBS Browser source shows. Butthurt reads these
files every time the page loads, so after an edit just refresh the Browser
source in OBS (right click it > Refresh, or "Refresh cache of current page").

  index.html   The page itself. The ids (#wrap, #alerts, #queue) are used by
               overlay.js, so don't rename or remove them.
  overlay.css  How everything looks. The safest place to start: the block at
               the top (":root") has the colors, fonts, sizes and timings.
  overlay.js   Talks to Butthurt and draws the queue and the announcements.
               Only touch this if you know JavaScript.

You can also drop your own images or fonts in here and use them from the CSS
(for example: background: url("my-frame.png");).

BROKE IT?
  Close Butthurt, delete this whole "overlay" folder and start Butthurt again.
  It puts the original files back. Any file that is missing is also served
  from the built-in copy, so deleting just one file restores just that one.

NOTE
  Updating Butthurt may replace these files with the new version. Keep your
  own copy of anything you changed.

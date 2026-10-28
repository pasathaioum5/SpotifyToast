# Bundled fonts

`GoogleSans-Regular.ttf` and `GoogleSans-Medium.ttf` are modified versions of **Google Sans** by The Google Sans Project Authors, licensed under the [SIL Open Font License 1.1](OFL.txt). "Google" and "Google Sans" are trademarks of Google LLC. This project isn't affiliated with or endorsed by Google.

Source: `ofl/googlesans/GoogleSans[GRAD,opsz,wght].ttf` from [google/fonts](https://github.com/google/fonts/tree/main/ofl/googlesans).

Changes from the source font:

1. Static instances were made from the variable font with [fontTools](https://github.com/fonttools/fonttools):
   ```
   fonttools varLib.instancer "GoogleSans[GRAD,opsz,wght].ttf" wght=400 opsz=18 GRAD=0 --static --update-name-table -o GoogleSans-Regular.ttf
   fonttools varLib.instancer "GoogleSans[GRAD,opsz,wght].ttf" wght=500 opsz=18 GRAD=0 --static --update-name-table -o GoogleSans-Medium.ttf
   ```
2. `OS/2.usWinAscent` and `usWinDescent` were set to 1050 and 300. The originals (1509 and 1079) make Windows GDI reserve a line height of about 2.6 em, which pushed the artist line out of the toast.

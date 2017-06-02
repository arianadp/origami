@ECHO off

SET UGII_USER_DIR=%~dp0
cd ..
START "Visual Studio" Project1.sln
EXIT 0
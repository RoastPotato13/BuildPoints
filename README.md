**Why BuildPoints?**

BuildPoints is an alternative construction time concept to Kerbal Construction Time. The goal is to provide a simpler setup, which integrates with stock systems, and doesn’t require as much micromanagement/future planning. You can launch crafts directly from the VAB/SPH and use the stock revert to VAB/SPH. You can run your current mission without worrying about what’s waiting in the queue. This is a good mod for you if you want to force time to pass between launches and are okay with abstracting the details to get there for simpler gameplay.

**How Does it Work?**

BuildPoints works by adding an additional resource to the game called “BuildPoints” which are needed to build any new craft. You passively accrue BuildPoints over time. As long as you have enough build points at the time of your launch, everything will behave exactly the same as stock with the BuildPoints costs deducted from your total. I view this as your manufacturing team is working on “something” but you don’t have to decide what that “something” actually is until you are ready to launch. A bit less realistic, but more flexible to use. If you do not have enough BuildPoints available, a pop-up will allow you to automatically time warp until you do. The settings are fairly flexible for you to come up with the exact cost setup you prefer.

**Screenshots**

BuildPoints Toolbar Icon:
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/GameData/BuildPoints/BuildPointsIcon.png "BP Icon")

Space Center Build Points Display: <br>
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/My%20Files/SpaceCenter_BPDisplay.png "BP Display")

Space Center Settings Tab: <br>
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/My%20Files/SpaceCenter_Settings.png "Settings")

Space Center Main Tab: <br>
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/My%20Files/SpaceCenter_BuildPoint.png "Main Menu")

VAB Calculator: <br>
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/My%20Files/VAB.png "VAB Calculator")

VAB Auto Warp Function: <br>
![alt text](https://github.com/RoastPotato13/BuildPoints/blob/master/My%20Files/VAB_AutoWarp.png "Auto Warp")

**Additional Settings Info**

- There is a “StartingPoints” setting only editable in the GlobalSettings.cfg if you want to start with BuildPoints (The default is 0).
- The default time warp method (unchecked) exits the VAB/SPH and time warps in the Space Center until there are enough BuildPoints to build the craft. This is the safest option. Checking the “instant time-skip” time warp method instead overwrites the current time in the persistent save file and accrues the corresponding BuildPoints before immediately launching. I’m not entirely sure if this won’t cause issues in stock, but my understanding is it should work unless you have mods which are tracking resources in the background. The benefit being you don’t have to deal with 2 extra scene switches and waiting for the time warp.
- FYI The default time warp method does automatically save your current craft under the name "BuildPoints - continue here". I'm not sure it's necessary, but I left it for now.

**Dependencies**

-Harmony 2

**Incompatibilities**

-I'm not aware of any incompatibilities for now. I would avoid other construction time mods like KCT as they provide the same function.

**Notes**

- As far as I can tell the release is stable to play. I am going to be testing for edge cases and to refine the settings I use. Feel free to leave feedback/issues you run into!
- This is my first mod, so hopefully there aren’t too many kinks along the way.
- I did use Claude to help write the code.

        public static bool DisplayPicture(string path, float duration, string animation, bool toQueue, bool cleanbg)
        { 
            try
            {
                if (string.IsNullOrEmpty(path))
                    return false;
        
                // Validate file path and existence
                string localPath;
                localPath = HttpUtility.UrlDecode(
                    Path.IsPathRooted(path)
                        ? Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path))
                        : Path.Combine(AppSettings.artworkPath, 
                            Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path)))
                );

                // If path is gGameMarquee, then extensions are only static (no video) pictures
                List<string> extensions = null;
                if (path == GetGameMarquee())
                {
                    // List of possible extensions for a static marquee
                    extensions = new List<string> { ".png", ".jpg", ".bmp", ".jpeg" };
                    LogIt($"⚙️ Setting marquee to: {path}");
                }
                else
                {
                    // List of possible extensions for other
                    extensions = new List<string> { ".gif", ".avi", ".mp4", ".png", ".jpg", ".bmp", ".apng", ".jpeg" };
                }

                // Find the file to display
                if (!FileExistsWithExtensions(localPath, extensions, out string foundExtension))
                {
                    var matchedFile = FindBestFuzzyMatch(localPath, extensions);
                    if (!string.IsNullOrEmpty(matchedFile))
                    {
                        LogIt($"⚠️Exact match not found for {localPath}, but found {matchedFile} using fuzzy matching");
                        localPath = Path.Combine(
                            Path.GetDirectoryName(matchedFile),
                            Path.GetFileNameWithoutExtension(matchedFile)
                        );
                        foundExtension = Path.GetExtension(matchedFile);
                    }
                    else
                    {
                        LogIt($"❗ Picture not found {localPath}");
                        return false;
                    }
                }
        
                string fullPath = localPath + foundExtension;
                if (localPath.Contains("&"))
                {
                    LogIt($"❗ Can't display picture with '&' in the name {fullPath}.\nSolution is rename the file and replace '&' by '-' in file name - see https://github.com/DMDTools/DOF2DMD/issues/27");
                    return false;
                }
                bool isVideo = new List<string> { ".gif", ".avi", ".mp4", ".apng" }.Contains(foundExtension.ToLower());
                bool isImage = new List<string> { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(foundExtension.ToLower());
                if (!isVideo && !isImage)
                {
                    return false;
                }
        
                // Now that we've validated everything, process the display asynchronously
                _ = Task.Run(() =>
                {
                    LogIt($"🎞️DisplayPicture: Starting visualization of {path}, Duration: {duration}, cleanbg: {cleanbg}, toQueue: {toQueue}"); 
                    // Check if gDmdDevice is initialized
                    int retries = 10;
                    while (gDmdDevice == null && retries > 0)
                    {
                        Thread.Sleep(1000);
                        LogIt($"❗ Retrying DMD device initialization {retries} retries left");
                        retries--;
                    }

                    if (gDmdDevice == null)
                    {
                        LogIt("🛑DMD device initialization failed 10 retries");
                        return;
                    }

                    // If this picture needs to be queued AND there is an animation/text running BUT current animation/text is not meant to be infinite, 
                    // then add this picture and its parameters to the animation queue. The animation timer will take care of it
                    if (toQueue && _animationTimer != null && _currentDuration > 0)
                    {
                        lock (_animationQueueLock)
                        {
                            LogIt($"⏳Queuing {path} for display after current animation");
                            _animationQueue.Enqueue(new QueueItem(path, duration, animation, cleanbg));
                            LogIt($"⏳Queue has {_animationQueue.Count} items: {string.Join(", ", _animationQueue.Select(i => i.Path))}");
                            return;
                        }
                    }

                    System.Action displayAction = () =>
                    {
                        gDmdDevice.Clear = true;
                        try
                        {
                            // Clear existing resources
                            if (cleanbg)
                            {
                                _SequenceQueue.RemoveAllScenes();
                                gDmdDevice.Graphics.Clear(Color.Black);
                                _loopTimer?.Dispose();
                            }
                            
                            _scoreDelayTimer?.Dispose();
                            _scoreDelayTimer = null;
                            _scoreBoard.Visible = false;
                        }
                        catch (Exception ex)
                        {
                            LogIt($"⚠️ Warning: Error while clearing resources: {ex.Message}");
                            // Continue execution as this is not critical
                        }
                        Actor mediaActor;
                        try
                        {
                            mediaActor = isVideo ?
                                (Actor)gDmdDevice.NewVideo("Video: " + path, fullPath) :
                                (Actor)gDmdDevice.NewImage("Image: " + path, fullPath);

                            mediaActor.SetSize(gDmdDevice.Width, gDmdDevice.Height);
                        }
                        catch (Exception ex)
                        {
                            LogIt($"❌ Error creating media actor: {ex.Message}");
                            return;
                        }

                        // Set random position if the file name contains "expl" (explosion?)
                        if (fullPath.Contains("expl"))
                        {
                            mediaActor.SetPosition(new Random().Next(-1, 2) * 32, 0);

                        }
                        
                        // Handle looping for GIFs/Videos when duration is -1
                        bool videoLoop = false;
                        if (isVideo && duration < 0)
                        {
                            LogIt($"🔄 Setting video loop to true for {fullPath}");
                            videoLoop = true;
                        }
                        _currentDuration = duration;
                        // If duration is negative - show immediately and clear the animation queue
                        if (duration < 0)
                        {
                            lock (_animationQueueLock)
                            {
                                _animationQueue.Clear();
                                LogIt($"⏳Animation queue cleared as duration was negative (immediate display, infinite duration)");
                            }
                            //duration = -1;
                        }

                        // Adjust duration for videos and images if not explicitly set
                        // For image, set duration to infinite (9999s)
                        duration = (isVideo && duration == 0) ? ((AnimatedActor)mediaActor).Length :
                                   (isImage && duration == 0) ? 9999 : duration;

                       
                        //Check the video Loop
                        duration = (videoLoop) ? -1 : duration;

                        BackgroundScene bg = CreateBackgroundScene(gDmdDevice, mediaActor, animation.ToLower(), duration, path);
                        _currentScene = bg; // Store reference to current scene
                        _SequenceQueue.Visible = true;

                        // Add scene to the queue or directly to the stage
                        if (cleanbg)
                        {
                            LogIt($"🎞️DisplayPicture: cleanbg is true, enqueuing {path} in _SequenceQueue"); 
                            _SequenceQueue.Enqueue(bg);
                            _loopTimer?.Dispose();
                        }
                        else
                        {
                            LogIt($"🎞️DisplayPicture: cleanbg is false, adding {path} to Stage");
                            gDmdDevice.Stage.AddActor(bg);
                        }
                        
                        // Arm timer once animation is done playing
                                               
                        if (duration >= 0) // Verificar si la duración es no negativa
                        {
                            _animationTimer?.Dispose(); 
                            _animationTimer = new Timer(AnimationTimer, null, (int)(duration * 1000), Timeout.Infinite);
                        }
                    };
                                            
                    LogIt($"📷Rendering {(isVideo ? $"video (duration: {duration * 1000}ms)" : "image")}: {fullPath}");
                    
                     // Execute initial action
                    gDmdDevice.Post(displayAction);
                    
                });
                
                // Return true immediately after validation, while display processing continues in background
                return true;
            }
            catch (Exception ex)
            {
                LogIt($"⚠️Error occurred while fetching the image. {ex.Message}");
                return false;
            }
        }

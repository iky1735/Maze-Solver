//***********************************************************************************************************************************************
//Author:       Jongwan(Evan) Yoon
//Description:  Loads a bitmap onto the canvas, which we use to decide on the state of the pixel (wall, visited, path) to determine a valid path
//              to solve the maze. Uses only a recursive method to solve the maze, and also uses a thread for bigger maps to make sure we can solve
//              the maze without hitting the limit for our system memory
//***********************************************************************************************************************************************
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using GDIDrawer;
using System.IO;

namespace CMPE2300ICA1
{
    public partial class Form1 : Form
    {
        readonly Stopwatch _stop = new Stopwatch();  //tracks how long it took to solve the maze
        CDrawer _canvas = null; //initializes the camvas
        Wall[,] _state = null;  //initialozes the enum, which will be used when scanning the bitmap
        MazeInfo _mazeDets;     //stores all the information about the loaded maze,
        bool _cancel = false;   //flag used to break out of the recursive method
        int _throttle;          //user set value to manipulate solving time
        int _scale = 10;        //scale which will change depending on the size of the bitmap

        public Form1()
        {
            InitializeComponent();
        }

        //will be used to determine and update the maze information while solving
        struct MazeInfo
        {
            public Point startPos;  //x, y position of the start position of the loaded maze
            public Point endPos;    //x, y position of the end position of the loaded maze
            public int mazeWidth;   //the width of the bitmap loaded onto the canvas
            public int mazeHeight;  //the height of the bitmap loaded onto the canvas
            public int steps;       //the steps taken to solve the maze (steps of the valid path)
            public Color livePath;  //the path will be coloured Orange if the path currently on is live
            public Color deadPath;  //the path will be coloured Gray if the path is dead (dead end encountered)
            public bool result;     //sees whether or not the maze was solved or not
        }

        //will be used to determine the surrounding areas of current position of the maze
        enum Wall
        {
            open,   //live path (White)
            wall,   //wall (Black)
            visited //visited path (Gray)
        }

        //Occurs everytime the 'Load' button is pressed. Attempts to open a bitmap file onto the canvas
        //Side-Effect: sets the maze's information while scanning the bitmap
        private void BtnLoad_Click(object sender, EventArgs e)
        {
            int x, y;   //position of the pixels on bitmap
            bool good = true;   //used to see if the file we are trying to open is valid

            OpenFileDialog openFile = new OpenFileDialog();
            string path = Path.GetFullPath(Environment.CurrentDirectory + @"\..\..\mazes");

            //initial settings for the open file dialog
            openFile.InitialDirectory = path;
            openFile.Title = "Load Bitmap maze to solve";
            openFile.Filter = "Bitmaps (*.bmp)|*.bmp|All files (*.*)|*.*";
            
            Bitmap map = null;
            _mazeDets = new MazeInfo
            {
                livePath = Color.Orange,
                deadPath = Color.Gray
            };

            //checked to see if the file was able to open
            try
            {
                //user chooses a file to open
                if (openFile.ShowDialog() == DialogResult.OK)
                {
                    //loads the bitmap file
                    map = new Bitmap(openFile.FileName);

                    //checks the valid size(x, y) of the bitmap file
                    try
                    {//sets the scale as an attempt to fit the bitmap onto the canvas
                        if (map.Height * map.Width < 10000)
                            _scale = 10;
                        else if (map.Height * map.Width < 20000)
                            _scale = 8;
                        else if (map.Height * map.Width < 150000)
                            _scale = 3;
                        else if (map.Height * map.Width < 450000)
                            _scale = 2;
                        else if (map.Height * map.Width < 1260000)
                            _scale = 1;
                        else
                            throw new Exception();
                    }

                    //error message for the bitmap file that is too big
                    catch (Exception)
                    {
                        MessageBox.Show("Bitmap size exceeds displayable area", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        good = false;
                    }
                }

                //user decided to close the open file dialog
                else
                    good = false;
            }

            //error message when the file chosen could not be opened
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                good = false;
            }

            if (good)
            {
                //creates a 2D array of enumeration to set the state of the pixel (open, wall, visited)
                _state = new Wall[map.Height + 1, map.Width + 1];
                _mazeDets.mazeHeight = map.Height;
                _mazeDets.mazeWidth = map.Width;

                if (_canvas != null)
                    _canvas.Close();

                //new canvas with appropriately adjusted size
                _canvas = new CDrawer(map.Width * _scale, map.Height * _scale, false)
                {
                    Scale = _scale,
                    BBColour = Color.White
                };

                //loads the scaled pixels from bitmap onto CDrawer canvas
                //depending on the colour of the pixel, decide the state and put it in 2D Enum array
                for (y = 0; y < map.Height; y++)
                {
                    for (x = 0; x < map.Width; x++)
                    {
                        //black pixel found? It's a wall
                        if (map.GetPixel(x, y) == Color.FromArgb(255, 0, 0, 0))
                            _state[y, x] = Wall.wall;

                        //white pixel found? It's an open path
                        else if (map.GetPixel(x, y) == Color.FromArgb(255, 255, 255))
                            _state[y, x] = Wall.open;

                        //finds position of start of the maze (green pixel)
                        if (map.GetPixel(x, y) == Color.FromArgb(0, 255, 0))
                        {
                            _mazeDets.startPos.X = x;
                            _mazeDets.startPos.Y = y;
                        }

                        //finds the position of end of the maze (red pixel)
                        if (map.GetPixel(x, y) == Color.FromArgb(255, 0, 0))
                        {
                            _mazeDets.endPos.X = x;
                            _mazeDets.endPos.Y = y;
                        }

                        _canvas.SetBBScaledPixel(x, y, map.GetPixel(x, y));
                    }
                }
                //sets the postion of the canvas so it loads right next to the form
                _canvas.Position = new Point(Location.X + Size.Width + 10, Location.Y);
                _canvas.Render();
                btnSolve.Enabled = true;
                listMazeLog.Items.Insert(0, $"Loaded: {Path.GetFileName(openFile.FileName)}");
            }
        }

        //Occurs when the 'Solve' button is pressed. Used to use the recursive method to go through the maze
        //Side Effect: might create a new thread with increased memory size. Will add new informations onto the listbox
        private void BtnSolve_Click(object sender, EventArgs e)
        {
            _stop.Restart();
            _cancel = false;
            btnLoad.Enabled = false;

            //determines whether to use a thread or not to solve the maze
            if (_mazeDets.mazeHeight * _mazeDets.mazeWidth > 4000 || _throttle > 4)
            {
                _stop.Start();

                //adds information of the maze onto the listbox
                this.Invoke(new MethodInvoker(delegate () { listMazeLog.Items.Insert(0, $"Attempting threaded solve of {_mazeDets.mazeWidth}X{_mazeDets.mazeHeight} maze"); }));
                Thread mazeThread = new Thread(new ThreadStart(ThreadMaze), 20000000)
                {
                    IsBackground = true
                };
                mazeThread.Start();

                //checks to see if the 'cancel' button was pressed. If it was, break out of the method
                if (btnSolve.Text == "Cancel")
                {
                    btnSolve.Text = "Solve";
                    btnSolve.Enabled = false;
                    btnLoad.Enabled = true;
                    _cancel = true;
                    listMazeLog.Items.Insert(0, $"Cancelled solving the maze");
                }

                else
                {
                    _cancel = false;
                    btnSolve.Text = "Cancel";
                }
            }

            //non-threaded version
            else
            {
                _stop.Start();
                listMazeLog.Items.Insert(0, $"Attempting non-threaded solve of {_mazeDets.mazeWidth}X{_mazeDets.mazeHeight} maze");
                MazeSolver(_mazeDets, _mazeDets.startPos);
                btnLoad.Enabled = true;

                //checks if the maze has an exit or not
                if (!_mazeDets.result)
                {
                    listMazeLog.Items.Insert(0, "Maze could not be solved. No exit found");
                    MessageBox.Show("This maze has no exit");
                }
            }
        }

        //***********************************************************************************************************************************************
        //Purpose:  recursive method as an attempt to solve a loaded maze, checking surrounding positions to find the valid path.
        //Parameter:MazeInfo maze - used to determine the current state of maze (solved or not) and the steps taken to solve the maze
        //          Point curPos  - the moved position to check if the current position is a valid path to take
        //Returns:  The maze's updated detail will be returned to check if the maze was solved or not
        //***********************************************************************************************************************************************
        private MazeInfo MazeSolver(MazeInfo maze, Point curPos)
        {
            if (!_mazeDets.result && !_cancel)
            {
                Thread.Sleep(_throttle);

                //exit conditions
                if (curPos.X == maze.endPos.X && curPos.Y == maze.endPos.Y)
                {
                    _stop.Stop();
                    _mazeDets.result = true;

                    //changes the state and label of buttons
                    this.Invoke(new MethodInvoker(delegate () { btnSolve.Enabled = false; }));
                    this.Invoke(new MethodInvoker(delegate () { btnSolve.Text = "Solve"; }));
                    this.Invoke(new MethodInvoker(delegate () { btnLoad.Enabled = true; }));

                    //adds the collected final information to the listbox
                    this.Invoke(new MethodInvoker(delegate () { listMazeLog.Items.Insert(0, $"The maze is solved in {maze.steps} steps / {_stop.ElapsedMilliseconds}ms"); }));

                    return maze;
                }

                //out of bounds -> return to previous position
                //wall/visited -> return to previous position
                if (curPos.X < 0 || curPos.X >= maze.mazeWidth || curPos.Y < 0 || curPos.Y >= maze.mazeHeight ||
                    _state[curPos.Y, curPos.X] == Wall.wall || _state[curPos.Y, curPos.X] == Wall.visited)
                {
                    return maze;
                }

                //will not colour the starting green pixel to another colour
                if (curPos.X != maze.startPos.X || curPos.Y != maze.startPos.Y)
                {
                    _canvas.SetBBScaledPixel(curPos.X, curPos.Y, maze.livePath);
                    _canvas.Render();
                }

                maze.steps += 1;
                _state[curPos.Y, curPos.X] = Wall.visited;

                //attempt to move position
                MazeSolver(maze, new Point(curPos.X + 1, curPos.Y));
                MazeSolver(maze, new Point(curPos.X - 1, curPos.Y));
                MazeSolver(maze, new Point(curPos.X, curPos.Y - 1));
                MazeSolver(maze, new Point(curPos.X, curPos.Y + 1));

                //reached a dead end, colour the return path to grey
                if (_mazeDets.result == false)
                {
                    _canvas.SetBBScaledPixel(curPos.X, curPos.Y, maze.deadPath);
                    _canvas.Render();
                }
            }
            return _mazeDets;
        }

        //Occurs when the value for the thread.sleep() changes, which would change the time it takes for the program to solve the maze
        //Side Effects: changes the interval of the recursive method
        private void Interval_ValueChanged(object sender, EventArgs e)
        {
            _throttle = (int)Interval.Value;
        }

        //***********************************************************************************************************************************************
        //Purpose:  called in to solve the loaded maze using a thread
        //Parameter:Nothing
        //Returns:  Nothing
        //***********************************************************************************************************************************************
        private void ThreadMaze()
        {
            MazeSolver(_mazeDets, _mazeDets.startPos);

            //checks if the maze was solved or not
            if (!_mazeDets.result && btnSolve.Text == "Cancel")
            {
                //changes the state and label of buttons
                this.Invoke(new MethodInvoker(delegate () { btnSolve.Enabled = false; }));
                this.Invoke(new MethodInvoker(delegate () { btnSolve.Text = "Solve"; }));
                this.Invoke(new MethodInvoker(delegate () { btnLoad.Enabled = true; }));
                Invoke(new MethodInvoker(delegate () { listMazeLog.Items.Insert(0, "Maze could not be solved. No exit found"); }));
                MessageBox.Show("This maze has no exit");
            }
        }
    }
}

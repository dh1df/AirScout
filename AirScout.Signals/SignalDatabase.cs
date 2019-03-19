﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Data;
using System.Diagnostics;
using ScoutBase.Core;
using System.Data.SQLite;
using Newtonsoft.Json;
using System.Windows.Forms;
using System.ComponentModel;

namespace AirScout.Signals
{

    public class SignalData
    {
        static SignalDatabase signals = new SignalDatabase();
        public static SignalDatabase Database
        {
            get
            {
                return signals;
            }
        }

    }

    /// <summary>
    /// Holds the signal information in a database structure.
    /// </summary>
    public class SignalDatabase
    {
        System.Data.SQLite.SQLiteDatabase db;

        private string DBPath;

        private static LogWriter Log = LogWriter.Instance;

        public readonly int UserVersion = 1;

        public SignalDatabase()
        {
            db = OpenDatabase("signals.db3");
            // set auto vacuum mode to "Full" to allow database to reduce size on disk
            // requires a vacuum command to change database layout
            AUTOVACUUMMODE mode = db.GetAutoVacuum();
            if (mode != AUTOVACUUMMODE.FULL)
            {
                if (MessageBox.Show("A major database layout change is necessary to run this version of AirScout. Older versions of AirScout are not compatible anymore and will cause errors. \n\nPress >OK< to start upgrade now (this will take some minutes). \nPress >Cancel< to leave.", "Database Upgrade of " + Path.GetFileName(db.DBLocation), MessageBoxButtons.OKCancel) == DialogResult.Cancel)
                    Environment.Exit(-1);                 //  exit immediately
                db.SetAutoVacuum(AUTOVACUUMMODE.FULL);
            }
            // create tables with schemas if not exist
            if (!SignalLevelTableExists())
                SignalLevelCreateTable();
        }

        ~SignalDatabase()
        {
            CloseDatabase(db);
        }

        private System.Data.SQLite.SQLiteDatabase OpenDatabase(string name)
        {
            System.Data.SQLite.SQLiteDatabase db = null;
            try
            {
                // check if database path exists --> create if not
                if (!Directory.Exists(DefaultDatabaseDirectory()))
                    Directory.CreateDirectory(DefaultDatabaseDirectory());
                // check if database is already on disk 
                DBPath = DefaultDatabaseDirectory();
                if (!File.Exists(Path.Combine(DBPath, name)))
                {
                    // create one on disk
                    System.Data.SQLite.SQLiteDatabase dbn = new System.Data.SQLite.SQLiteDatabase(Path.Combine(DBPath, name));
                    // open database
                    dbn.Open();
                    // set user version
                    dbn.SetUserVerion(UserVersion);
                    // set auto vacuum mode to full
                    dbn.SetAutoVacuum(AUTOVACUUMMODE.FULL);
                    dbn.Close();
                }
                // check for in-memory database --> open from disk, if not
                if (Properties.Settings.Default.Database_InMemory)
                    db = System.Data.SQLite.SQLiteDatabase.CreateInMemoryDB(Path.Combine(DBPath, name));
                else
                {
                    db = new System.Data.SQLite.SQLiteDatabase(Path.Combine(DBPath, name));
                    db.Open();
                }
                // get version info
                int v = db.GetUserVersion();
                // do upgrade stuff here
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initilalizing database: " + ex.Message);
                throw new TypeInitializationException(this.GetType().Name, ex);
            }
            return db;
        }

        private void CloseDatabase(System.Data.SQLite.SQLiteDatabase db)
        {
            if (db == null)
                return;
            // save in-memory database to disk
            if (db.IsInMemory)
                db.BackupDatabase(db.DBLocation);
            //            else
            //                db.Close();
        }

        public void BackupDatabase()
        {
            if (db == null)
                return;
            // save in-memory database to disk
            if (db.IsInMemory)
                db.BackupDatabase(db.DiskFileName);
            else
                db.Close();
        }

        public bool IsInMemory()
        {
            return db.IsInMemory;
        }

        public string DefaultDatabaseDirectory()
        {
            // create default database directory name
            string dir = Properties.Settings.Default.Database_Directory;
            if (!String.IsNullOrEmpty(dir))
            {
                return dir;
            }
            // empty settings -_> create standard path
            // collect entry assembly info
            Assembly ass = Assembly.GetExecutingAssembly();
            string company = "";
            string product = "";
            object[] attribs;
            attribs = ass.GetCustomAttributes(typeof(AssemblyCompanyAttribute), true);
            if (attribs.Length > 0)
            {
                company = ((AssemblyCompanyAttribute)attribs[0]).Company;
            }
            attribs = ass.GetCustomAttributes(typeof(AssemblyProductAttribute), true);
            if (attribs.Length > 0)
            {
                product = ((AssemblyProductAttribute)attribs[0]).Product;
            }
            // create database path
            dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!String.IsNullOrEmpty(company))
                dir = Path.Combine(dir, company);
            if (!String.IsNullOrEmpty(product))
                dir = Path.Combine(dir, product);
            return Path.Combine(dir, "AircraftData");
        }

        public DATABASESTATUS GetDBStatus()
        {
            if (db != null)
                return db.Status;
            return DATABASESTATUS.UNDEFINED;
        }

        public void SetDBStatus(DATABASESTATUS status)
        {
            if (db != null)
                db.Status = status;
        }

        public bool GetDBStatusBit(DATABASESTATUS statusbit)
        {
            if (db != null)
                return (((int)db.Status) & ((int)statusbit)) > 0;
            return false;
        }

        public void SetDBStatusBit(DATABASESTATUS statusbit)
        {
            if (db != null)
                db.Status |= statusbit;
        }

        public void ResetDBStatusBit(DATABASESTATUS statusbit)
        {
            if (db != null)
                db.Status &= ~statusbit;
        }

        public void BeginTransaction()
        {
            if (db == null)
                return;
            db.BeginTransaction();
        }

        public void Commit()
        {
            if (db == null)
                return;
            db.Commit();
        }

        private DataTable Select(System.Data.SQLite.SQLiteDatabase db, string sql)
        {
            return db.Select(sql);
        }

        public string GetDBLocation()
        {
            if (db == null)
                return "";
            return db.DBLocation;
        }

        public double GetDBSize()
        {
            if (db == null)
                return 0;
            return db.DBSize;
        }

        private bool IsValid(object obj)
        {
            if (obj == null)
                return false;
            if (obj.GetType() == typeof(DBNull))
                return false;
            return true;
        }

        #region SignalLevel

        public bool SignalLevelTableExists(string tablename = "")
        {
            // check for table name is null or empty --> use default tablename from type instead
            string tn = tablename;
            if (String.IsNullOrEmpty(tn))
                tn = SignalLevelDesignator.TableName;
            return db.TableExists(tn);
        }

        public void SignalLevelCreateTable(string tablename = "")
        {
            lock (db.DBCommand)
            {
                // check for table name is null or empty --> use default tablename from type instead
                string tn = tablename;
                if (String.IsNullOrEmpty(tn))
                    tn = SignalLevelDesignator.TableName;
                db.DBCommand.CommandText = "CREATE TABLE `" + tn + "`(Level DOUBLE, LastUpdated INT32, PRIMARY KEY (LastUpdated))";
                db.DBCommand.Parameters.Clear();
                db.Execute(db.DBCommand);
            }
        }

        public long SignalLevelCount()
        {
            object count = db.ExecuteScalar("SELECT COUNT(*) FROM " + SignalLevelDesignator.TableName);
            if (IsValid(count))
                return (long)count;
            return 0;
        }

        public bool SignalLevelExists(DateTime lastupdated)
        {
            SignalLevelDesignator sd = new SignalLevelDesignator(lastupdated);
            return SignalLevelExists(sd);
        }

        public bool SignalLevelExists(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "SELECT EXISTS (SELECT LastUpdated FROM " + SignalLevelDesignator.TableName + " WHERE LastUpdated = @LastUpdated";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                object result = db.DBCommand.ExecuteScalar();
                if (IsValid(result) && ((long)result > 0))
                    return true;
            }
            return false;
        }

        public SignalLevelDesignator SignalLevelFind(DateTime lastupdated)
        {
            SignalLevelDesignator sd = new SignalLevelDesignator(lastupdated);
            return SignalLevelFind(sd);
        }

        public SignalLevelDesignator SignalLevelFind(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "SELECT * FROM " + SignalLevelDesignator.TableName + " WHERE LastUpdated = @LastUpdated";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                try
                {
                    DataTable Result = db.Select(db.DBCommand);
                    if ((Result != null) && (Result.Rows.Count > 0))
                    {
                        return new SignalLevelDesignator(Result.Rows[0]);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteMessage(ex.ToString());
                }
            }
            return null;
        }

        public SignalLevelDesignator SignalLevelFindAt(long index)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "SELECT * FROM " + SignalLevelDesignator.TableName + " LIMIT 1 OFFSET " + index.ToString();
                db.DBCommand.Parameters.Clear();
                try
                {
                    DataTable Result = db.Select(db.DBCommand);
                    if ((Result != null) && (Result.Rows.Count > 0))
                    {
                        return new SignalLevelDesignator(Result.Rows[0]);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteMessage(ex.ToString());
                }
            }
            return null;
        }

        public DateTime SignalLevelFindlastUpdated(DateTime lastupdated)
        {
            SignalLevelDesignator sd = new SignalLevelDesignator(lastupdated);
            return SignalLevelFindLastUpdated(sd);
        }

        public DateTime SignalLevelFindLastUpdated(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "SELECT LastUpdated FROM " + SignalLevelDesignator.TableName + " WHERE LastUpdated = @LastUpdated";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                object result = db.ExecuteScalar(db.DBCommand);
                if (IsValid(result))
                    return (SQLiteEntry.UNIXTimeToDateTime((int)result));
            }
            return DateTime.MinValue;
        }

        public int SignalLevelInsert(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "INSERT INTO " + SignalLevelDesignator.TableName + " (Level, LastUpdated) VALUES (@Level, @LastUpdated)";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsDouble("Level"));
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                return db.ExecuteNonQuery(db.DBCommand);
            }
        }

        public int SignalLevelDelete(DateTime lastupdated)
        {
            SignalLevelDesignator sd = new SignalLevelDesignator(lastupdated);
            return SignalLevelDelete(sd);
        }

        public int SignalLevelDelete(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "DELETE FROM " + SignalLevelDesignator.TableName + " WHERE LastUpdated = @LastUpdated";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                return db.ExecuteNonQuery(db.DBCommand);
            }
        }

        public int SignalLevelDeleteAll()
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "DELETE FROM " + SignalLevelDesignator.TableName;
                db.DBCommand.Parameters.Clear();
                return db.ExecuteNonQuery(db.DBCommand);
            }
        }

        public int SignalLevelUpdate(SignalLevelDesignator sd)
        {
            lock (db.DBCommand)
            {
                db.DBCommand.CommandText = "UPDATE " + SignalLevelDesignator.TableName + " SET Level = @Level, LastUpdated = @LastUpdated WHERE LastUpdated = @LastUpdated";
                db.DBCommand.Parameters.Clear();
                db.DBCommand.Parameters.Add(sd.AsDouble("Level"));
                db.DBCommand.Parameters.Add(sd.AsUNIXTime("LastUpdated"));
                return db.ExecuteNonQuery(db.DBCommand);
            }
        }

        public int SignalLevelBulkInsert(List<SignalLevelDesignator> sds)
        {
            int errors = 0;
            try
            {
                lock (db)
                {
                    db.BeginTransaction();
                    foreach (SignalLevelDesignator sd in sds)
                    {
                        try
                        {
                            SignalLevelInsert(sd);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteMessage("Error inserting SignalLevel at [" + sd.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss") + "]: " + ex.ToString());
                            errors++;
                        }
                    }
                    db.Commit();
                }
            }
            catch (Exception ex)
            {
                Log.WriteMessage(ex.ToString());
            }
            return -errors;
        }

        public int SignalLevelBulkDelete(List<SignalLevelDesignator> sds)
        {
            int errors = 0;
            try
            {
                lock (db)
                {
                    db.BeginTransaction();
                    foreach (SignalLevelDesignator sd in sds)
                    {
                        try
                        {
                            SignalLevelDelete(sd);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteMessage("Error deleting SignalLevel at [" + sd.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss") + "]: " + ex.ToString());
                            errors++;
                        }
                    }
                    db.Commit();
                }
            }
            catch (Exception ex)
            {
                Log.WriteMessage(ex.ToString());
            }
            return -errors;
        }

        public int SignalLevelBulkInsertOrUpdateIfNewer(List<SignalLevelDesignator> sds)
        {
            if (sds == null)
                return 0;
            int i = 0;
            lock (db)
            {
                db.BeginTransaction();
                foreach (SignalLevelDesignator sd in sds)
                {
                    SignalLevelInsertOrUpdateIfNewer(sd);
                    i++;
                }
                db.Commit();
            }
            return i;
        }

        public int SignalLevelInsertOrUpdateIfNewer(SignalLevelDesignator sd)
        {
            DateTime dt = SignalLevelFindLastUpdated(sd);
            if (dt == DateTime.MinValue)
                return SignalLevelInsert(sd);
            if (dt < sd.LastUpdated)
                return SignalLevelUpdate(sd);
            return 0;
        }

        public List<SignalLevelDesignator> SignalLevelGetAll()
        {
            List<SignalLevelDesignator> l = new List<SignalLevelDesignator>();
            DataTable Result = db.Select("SELECT * FROM " + SignalLevelDesignator.TableName);
            if (!IsValid(Result) || (Result.Rows.Count == 0))
                return l;
            foreach (DataRow row in Result.Rows)
                l.Add(new SignalLevelDesignator(row));
            return l;

        }

        public List<SignalLevelDesignator> SignalLevelFromJSON(string json)
        {
            if (String.IsNullOrEmpty(json))
                return null;
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            settings.FloatFormatHandling = FloatFormatHandling.String;
            settings.Formatting = Newtonsoft.Json.Formatting.Indented;
            return JsonConvert.DeserializeObject<List<SignalLevelDesignator>>(json, settings);
        }

        public string SignalLevelToJSON()
        {
            List<SignalLevelDesignator> l = SignalLevelGetAll();
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            settings.FloatFormatHandling = FloatFormatHandling.String;
            settings.Formatting = Newtonsoft.Json.Formatting.Indented;
            string json = JsonConvert.SerializeObject(l, settings);
            return json;
        }

        public DataTableSignalLevels SignalLevelToDataTable()
        {
            List<SignalLevelDesignator> sds = SignalLevelGetAll();
            DataTableSignalLevels dtl = new DataTableSignalLevels(sds);
            return dtl;
        }

        #endregion

    }

}

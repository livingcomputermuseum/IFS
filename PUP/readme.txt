Readme.txt for IFS v1.2:

1. Introduction and Overview
============================

In the 1970s, Xerox PARC developed a set of protocols based around the "PUP"
(the "PARC Universal Packet").  These were intended to be a stopgap until
something "real" could be designed and implemented, so the suite was referred 
to as "IFS" ("Interim File Server").  That "real" implementation never came
into being during the Alto's lifetime, so the IFS was a permanent fixture of
the network environment at PARC during the heyday of the Alto.

The LCM+L's IFS implementation is an implementation of this protocol suite
that runs on a modern PC, and is designed to work with the ContrAlto Alto
emulator over either Raw Ethernet packets or UDP broadcasts.

It provides the following IFS services:

  - BreathOfLife:   Provides the "Breath Of Life" packet needed to bootstrap
                    an Alto over the network.
  - EFTP/Boot:      Provides boot files over the network.
  - FTP:            File Transfer Protocol.
  - CopyDisk:       Allows imaging and restoring of Alto disk packs over the 
                    network.
  - Misc. Services: Provides network name lookup, time and other miscellaneous
                    services.
  - Gateway:        Routes PUPs to other sites on the Internet
  - Mail:           Delivers mail to other users.  (Currently only on the same
                    network, mail is not routed.)


The following services are not yet provided, but are planned:

  - EFTP/Printing:  Provides print services to networked Altos
  - Mail routing:   Sending mail to other sites over the Internet.

If you have questions, or run into issues or have feature requests, please
feel free to e-mail me at joshd@livingcomputers.org.


1.1 Getting Started
-------------------

IFS does not provide an installer; unzip the archive to a directory on the
machine you wish to use as a server.  Modify the configuration files in the
"Conf" directory to your liking (See Section 2.0) and run the "IFS.exe"
executable to run the IFS services.  The startup banner will print and you'll
be at the Console command prompt (See Section 3.0 for details).  The IFS
server is now running and is ready to serve your network of Altos.


1.2 Important Note
------------------

This IFS implementation is still a work in progress and should not be used for 
any mission-critical or security-critical purposes.  IFS protocols sent 
passwords in plain-text and in general were not designed with tight security 
in mind.  All files (even those in user directories) are globally readable.


2.0 Configuration
=================

IFS uses a set of files in the "Conf" subdirectory to configure the server.
These include:

    - accounts.txt:         Defines the set of user accounts
    - bootdirectory.txt:    Maps boot numbers to boot files for network boot
    - hosts.txt:            Maps Inter-network numbers to names
    - ifs.cfg:              General configuration for the IFS server

2.1 ifs.cfg:
------------

ifs.cfg contains general configuration details for the server.  It specifies
configuration for the network transport, directory paths and debugging options.

Directory configuration:
    - FTPRoot:      Specifies the path for the root of the FTP directory tree.
    - CopyDiskRoot:	Specifies the path for the directory to store CopyDisk 
                    images.
    - BootRoot:     Specifies the path for boot images.
    - MailRoot:     Specifies the path for the root of the Mail directory tree.
                    (User mail folders are placed in this directory.)

Interface configuration:
    - InterfaceType:    "RAW" or "UDP".  Specifies the transport to use for
                        communication.
    - InterfaceName:    The name of the host network adapter to use for 
                        communication.

    - UDPPort:          The port number (decimal) to use for the UDP transport.

Network configuration:
    - ServerNetwork:    The IFS server's network number.
    - ServerHost:       The IFS server's host number.

Debugging configuration:
    - LogTypes:         The level of verbosity for logging.  One of:
                        None, Normal, Warning, Error, Verbose, or All

    - LogComponent:     The components to log details about.  One of:
                        None, Ethernet, RTP, BSP, MiscServices, CopyDisk,
                        DirectoryServices, PUP, FTP, BreathOfLife, EFTP,
                        BootServer, UDP, Mail, Configuration, or All

2.2 hosts.txt:
--------------

hosts.txt maps Xerox "inter-network" names to hostnames.  If you're familiar
with /etc/hosts on UNIX-like machines, this will be very familiar.

Each line is either a comment (beginning with '#') or contains an inter-network
name and a hostname to associate with it, separated by whitespace.  At this 
time, each inter-network name maps to exactly one hostname.

A Xerox "inter-network name" defines a host number and a network number, and is
expressed in the format "<network>#<host>#", where both <network> and <host>
are octal values between 0 and 377.  So, for example, an Alto on network 5
with host number 72 would have an inter-network name of "5#72#".  Hosts on the
same network as the IFS server (see Section 2.1 for IFS server configuration)
need not specify the network number and have the format of "<host>#".

A Hostname is an alphanumeric sequence that must begin with a letter.

A hosts.txt entry for our Alto on network 5 with host number 72 providing said
system with name "alan" would thus look like:

5#72#       alan

Or optionally, if our IFS server is on network 5:

72#         alan

It is a good idea to provide an entry for the IFS server itself so that the 
server can easily be reached by name.  By default (unless ifs.cfg has been 
changed), the IFS server's inter-network name is 1#1# (network 1, host 1).


2.3 networks.txt
----------------
 
networks.txt identifies known networks and provides the address and port for 
their IFS gateway servers.  See Section 5 for more details on gateways.
This file is processed when IFS starts.

Each line in this file is of the format
 <inter-network number>  <IFS host IP or hostname>[:port]

For example:
   5#    192.168.1.137
would define network 5's gateway as 192.168.1.137 with the default port.

   12#   myhostname.net:6666
defines network 12's gateway at myhostname.net, port 6666.

If no port number is specified for a given network entry, the entry will
default to 42425.

networks.txt must contain an entry for the local IFS server itself if you
want to enable routing through the gateway.  In order for the IFS gateway 
to be able to talk to the outside world, the port specified for the local
IFS server must be opened.  (You may need to enable port forwarding if you
are going to be routing PUPs over the Internet, for example.)

2.4 bootdirectory.txt
---------------------

bootdirectory.txt maps boot numbers to the bootfile they correspond to.  The
file format is very similar to the hosts.txt file -- each line consists of
either a comment (again beginning with '#') or contains a boot file number (in 
octal) and the boot file to associate with it.

The boot files listed in bootdirectory.txt correspond to files placed in the 
BootRoot directory, as specified in ifs.cfg (see Section 2.1 for details).

By default bootdirectory.txt contains a set of entries that map to the file
numbers conventionally used at Xerox PARC.  This allows Alto programs and 
subsystems that rely on this convention to work properly. It is advisable
to leave these as is for this reason.  Anything past 100 (octal) is fair game
generally, configure those as you will.

Note that the IFS server does not include the actual boot files -- see Section
6.0 for details on where to find these files to populate your BootRoot 
directory.

2.5 accounts.txt
----------------

accounts.txt defines user accounts for the IFS system.  

Note: These accounts are unrelated to any user accounts that might be provided 
by the host system (i.e.a Windows or Linux box that is running IFS).  It is
*extremely* advisable to avoid using the same password for your IFS account 
that you do for any real-world account (see Section 1.2).

Each line in accounts.txt consists of either a comment (beginning with '#')
or a user definition.  If you're familiar with /etc/password on UNIX systems
this will look very familiar.

This file can be hand-edited using a text editor, but the file will be 
re-generated if the Console (see Section 3.0) is used to add or modify user
accounts, so comments will be lost.  It is generally advisable to use the 
console to add, remove, or change user accounts.

Each user definition is a line in the format:
<username>:<password hash>:<privileges>:<full user name>:<home directory>

    - username:         an alphanumeric sequence starting with a letter.  This
                        define's the user's login name.
    - password hash:    an encoded version of the user's password.  This can be
                        edited, but is generally not advisable.  See Section
                        3.0 for details on setting and changing user passwords.
    - privileges:       Either Admin (administrative privileges) or User (normal
                        user privileges).  See section 4.0 for details.
    - full user name:   Self explanatory; the full name (i.e. Alan Kay) of the
                        user.
    - home directory:   The user's directory (which is placed under the FTPRoot
                        directory).   See Section 4.0 for details on user 
                        directories.

Changes made to this file while IFS is running will not take effect until IFS
is restarted.  (This is another reason to use the Console to make changes --
see Section 3.0).

3.0 The Command Console
=======================

The IFS server provides a command console, designated by the ">" prompt.  The 
console provides numerous commands for managing the state of the server and
for managing user accounts.

The console provides simple command completion; hit the "TAB" key at any time
to see possible completions for the command you are entering.

The "show commmands" command displays a list of possible commands with brief
synopses and descriptions.

Here is a rundown of the basic command set:

    show users - Displays the current user database (See Section 4.0)

    show user <username> - Displays information for the specified user
                           (See Section 4.0)
    set password <username> - Sets the password for the specified user
                           (See Section 4.0)

    add user <username> <password> [User|Admin] <full name> <home directory> 
        - Adds a new user account  (See section 4.0 for details)			

    remove user <username> - Removes an existing user account (See Section 4.0)

    show active servers - Displays active server statistics.

    quit - Terminates the IFS process

    show commands - Shows console commands and their descriptions.


4.0 User Accounts, Authentication, and Security
===============================================

IFS provides a very simple model for user accounts, authentication and security.
It is by no means expected to be extensive or useful for all applications, nor
does it guarantee any level of security.  It provides a set of facilities for
creating user accounts and assigning passwords and coarsely-grained permissions,
approximating the environment as it would have existed at Xerox PARC.

Rephrasing the note in Section 1.2:  Do not rely on IFS for security or privacy.
Allow only trusted users into the network that hosts IFS, and use common sense.

4.1 User Accounts
-----------------

Each user account is defined by an entry in the accounts.txt file (as described
in section 2.4).

A new user account can be created either be editing the accounts.txt file, or
through use of the "add user" Console command.  The latter is recommended.

Each user can be assigned a password, or have his/her password changed using 
the "set password" command.

Every user gets his or her own user directory for files; this is a 
subdirectory of the FTPRoot directory and will be automatically created by the
"add user" Console command (See Setion 3.0) and removed by the "remove user"
command.

There are exactly two levels of privilege possible:  User and Admin.  A user
with "User" level privileges has read/write access to their own user file 
directory, and read-only access to all other file directories (including other user
directories).  A user with "Admin" privileges has read-write access to all 
directories.

Every user gets his or her own mail directory as a subdirectory
of the MailRoot directory.  This directory is automatically created the first
time a user sends or receives mail. A given user's mail directory is only 
readable (or writable) by that user, regardless of privilege.

Additionally, every user (including guest) has read access to disk images in 
the CopyDiskRoot directory.  Administrative users also have the ability to
write new images to this directory.


4.2 The Guest Account
---------------------

There is one special account, the "guest" account.  The guest account does not
have an entry in the accounts.txt file, and has no password.  The guest user
has read-only access, but has no user directory.

5.0 Network Transports: Care and Feeding
========================================

IFS simulates the original "Experimental" 3mbit Xerox Ethernet by encapsulating
3mbit packets in a modern transport, either via UDP broadcasts or via Raw 
Ethernet packets.

The transport type is configured in the ifs.cfg file (See Section 2.1) via the
"InterfaceType" parameter.  The UDP port can be defined by the "UDPPort" parameter,
if unspecified it defaults to 42424.

In both cases, packets are broadcast over the network.  This is done in order
to make the use of the simulated network easier and to keep the implementation
simple as well.  In general, Altos (simulated or otherwise) do not generate
large amounts of traffic as compared with the bandwidth of a modern network.  
(The maximum throughput of an Alto transferring a file via FTP, for example, 
is on the order of 20K/sec).  It may be a good idea to run your IFS server
and Altos on a separate network segment if you are concerned about network 
usage.

You cannot run both the IFS server and a ContrAlto emulator on the same machine
if they are configured to use UDP as the transport.

5.1 Gateways and Routing
------------------------

The original IFS at PARC provided Gateway services for routing PUPs across
multiple networks via various transports (ethernet, serial, modems and even
experimental wireless networks).  It supported multi-hop routing that was
in many ways similar to the modern Internet.

The LCM+L IFS server provides single-hop routing via UDP.  This allows 
the connection of multiple Alto networks together, either on a local network
or over the global Internet.

The networks involved are defined in the networks.txt configuration file
(see Section 2.3) and specify what IP address corresponds to the network in
question.  When the local IFS server receives a packet destined for another
network, it uses networks.txt to figure out what IP to send it to.  Similarly,
the local IFS server listens for incoming packets from other IFS servers and
routes them onto the local network.

Unlike the original IFS, routing is statically defined by networks.txt at
startup and cannot be changed at runtime.  If in the future the advantages 
of supporting a dynamic routing scheme outweigh the disadvantages (complication,
security, etc.) this may be added.

Additionally, routing is single-hop only.  The assumption is made that any
site on a TCP/IP network can reach any other via the Internet or local 
networking.  In effect, the real routing is done by TCP/IP, not IFS.  Multi-
hop routing would be an interesting exercise but seems superfluous and as usual
the decision was to err on the side of simplicity.

Important things to keep in mind when configuring routing:
    - Ensure all sites have unique network numbers:  make sure each IFS
      server has a unique network number in ifs.cfg
    - Ensure all IFS servers have entries in networks.txt (including the 
      local IFS server!)
    - Ensure the port you have specified for the local network in 
      networks.txt is open, and is accessible by other IFS servers.
    - It is useful (but not necessary) to have entries for IFS servers
      and Alto hosts in hosts.txt

If you need to debug routing, you can set "LogComponents" to "Routing" and
LogTypes to "All" in ifs.cfg.  This will cause incoming and outgoing PUPs to
be logged to the console as they are processed.

6.0 Where to Find Alto Files
============================

The IFS server distribution does not include any Alto files, but they can be
found in many places on the Internet.

Disk images (for use with the CopyDisk protocol) and some boot files can be 
found on Bitsavers, at http://bitsavers.org/bits/Xerox/Alto/.

A wide variety of files, including programs, documentation, and related data
can be found in the CHM's Xerox PARC Alto filesystem archive at 
http://xeroxalto.computerhistory.org/.  The raw files can be downloaded and
placed in your FTPRoot or BootRoot directories as desired.


7.0 Xerox Documentation Reference
=================================

The following documents may be useful in actually using the Alto-land client
tools (FTP, CopyDisk, mail, etc) to communicate with the IFS server:

The Alto User's Handbook: 
	http://bitsavers.org/pdf/xerox/alto/Alto_Users_Handbook_Sep79.pdf
Alto Subsystems: 
	http://bitsavers.org/pdf/xerox/alto/memos_1981/Alto_Subsystems_May81.pdf


The following specifications were used to implement the IFS protocol suite:

FTP: http://xeroxalto.computerhistory.org/_cd8_/pup/.ftpspec.press!2.pdf
Boot: http://xeroxalto.computerhistory.org/_cd8_/pup/.altoboot.press!1.pdf
CopyDisk: http://xeroxalto.computerhistory.org/_cd8_/pup/.copydisk.press!1.pdf
Gateway: 
http://xeroxalto.computerhistory.org/_cd8_/pup/.gatewayinformation.press!1.pdf
Misc Services: 
http://xeroxalto.computerhistory.org/_cd8_/pup/.miscservices.press!1.pdf


8.0 Packet-Level Protocol
=========================

IFS (and ContrAlto) use a very simple encapsulation for transmitting 3mbit 
Ethernet packets over modern transports.  An encapsulated packet consists of
two fields:  
	- Packet Length (2 bytes): Length (in 16-bit words) of the 3mbit Packet 
		Data field (see below)
	- Packet Data (N bytes): The 3mbit packet, including 3mbit Ethernet header
		but excluding the checksum word.

All words are stored in big-endian format.

The Packet Length field is necessary to allow proper transport over Raw
Ethernet packets -- modern Ethernet defines a minimum length for a packet
whereas the Experimental 3mbit Ethernet (as implemented on the Alto) does not.
Short packets are therefore padded by the modern transport, so a separate field
is necessary in order to ascertain the intended packet length.

As discussed in Section 5.0, all packets are broadcast.  The technical reasons
for this are documented in the source code; see Transport\UDP.cs for details.


9.0 Thanks
==========

This project would not have been possible without the conservation efforts of
the CHM and Bitsavers.
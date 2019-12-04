-- Version 1.7.1.2
-- Make sure you COPY this file to the same location as the Export.lua as well! 
-- Otherwise the Radio Might not work

net.log("Loading - DCS-SRS GameGUI - Ciribob: 1.7.1.2")
local SRS = {}

SRS.CLIENT_ACCEPT_AUTO_CONNECT = true --- Set to false if you want to disable AUTO CONNECT

SRS.unicast = true

SRS.dbg = {}
SRS.logFile = io.open(lfs.writedir()..[[Logs\DCS-SRS-GameGUI.log]], "w")
function SRS.log(str)
    if SRS.logFile then
        SRS.logFile:write(str.."\n")
        SRS.logFile:flush()
    end
end

package.path  = package.path..";.\\LuaSocket\\?.lua;"
package.cpath = package.cpath..";.\\LuaSocket\\?.dll;"
package.cpath = package.cpath..";"..lfs.writedir().."Mods\\tech\\DCS-SRS\\bin\\?.dll;"

local socket = require("socket")

local srs = nil

pcall(function()
	srs = require("srs")

	SRS.log("Loaded SRS.dll")
end)

if not srs then
	SRS.log("Couldnt load SRS.dll")
end

local JSON = loadfile("Scripts\\JSON.lua")()
SRS.JSON = JSON

SRS.UDPSendSocket = socket.udp()
SRS.UDPSendSocket:settimeout(0)

local _lastSent = 0;

SRS.onPlayerChangeSlot = function(_id)

    -- send when there are changes
    local _myPlayerId = net.get_my_player_id()

    if _id == _myPlayerId then
        SRS.sendUpdate(net.get_my_player_id())
    end
  
end

SRS.onSimulationFrame = function()

    local _now = DCS.getRealTime()

    -- send every 5 seconds
    if _now > _lastSent + 5.0 then
        _lastSent = _now 
     --    SRS.log("sending update")
        SRS.sendUpdate(net.get_my_player_id())
    end

end

SRS.sendUpdate = function(playerID)
  
    local _update = {
        name = "",
        side = 0,
    }

    _update.name = net.get_player_info(playerID, "name" )
	_update.side = net.get_player_info(playerID,"side")

    --SRS.log("Update -  Slot  ID:"..playerID.." Name: ".._update.name.." Side: ".._update.side)

	if SRS.unicast then
		socket.try(SRS.UDPSendSocket:sendto(SRS.JSON:encode(_update).." \n", "127.0.0.1", 5068))
	else
		socket.try(SRS.UDPSendSocket:sendto(SRS.JSON:encode(_update).." \n", "127.255.255.255", 5068))
	end


end

SRS.MESSAGE_PREFIX_OLD = "This server is running SRS on - " -- DO NOT MODIFY!!!
SRS.MESSAGE_PREFIX = "SRS Running @ " -- DO NOT MODIFY!!!

function string.startsWith(string, prefix)
    return string.sub(string, 1, string.len(prefix)) == prefix
end

function string.trim(_str)
    return string.format( "%s", _str:match( "^%s*(.-)%s*$" ) )
end

function SRS.isAutoConnectMessage(msg)
    return string.startsWith(string.trim(msg), SRS.MESSAGE_PREFIX) or string.startsWith(string.trim(msg), SRS.MESSAGE_PREFIX_OLD)
end

function SRS.getHostFromMessage(msg)
	if string.startsWith(string.trim(msg), SRS.MESSAGE_PREFIX_OLD) then
		return string.trim(string.sub(msg, string.len(SRS.MESSAGE_PREFIX_OLD) + 1))
	else
		return string.trim(string.sub(msg, string.len(SRS.MESSAGE_PREFIX) + 1))
	end
end

-- Register callbacks --

SRS.sendConnect = function(_message)

    if SRS.unicast then
        socket.try(SRS.UDPSendSocket:sendto(_message.."\n", "127.0.0.1", 5069))
    else
        socket.try(SRS.UDPSendSocket:sendto(_message.."\n", "127.255.255.255", 5069))
    end
end

SRS.onChatMessage = function(msg, from)

    -- Only accept auto connect message coming from host.
    if SRS.CLIENT_ACCEPT_AUTO_CONNECT
                        and from == 1
            and  SRS.isAutoConnectMessage(msg) then
        local host = SRS.getHostFromMessage(msg)
        SRS.log(string.format("Got SRS Auto Connect message: %s", host))

        local enabled = OptionsData.getPlugin("DCS-SRS","srsAutoLaunchEnabled")
        if srs and enabled then
            local path = srs.get_srs_path()
            if path ~= "" then

                net.log("Trying to Launch SRS @ "..path)
                srs.start_srs(host)
            end

        end
        SRS.sendConnect(host)
    end
end


DCS.setUserCallbacks(SRS)

net.log("Loaded - DCS-SRS GameGUI - Ciribob: 1.7.1.2")
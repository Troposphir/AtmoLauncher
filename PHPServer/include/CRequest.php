<?php
/*==============================================================================
  Troposphir - Part of the Troposphir Project
  Copyright (C) 2016  Troposphir Development Team

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as
  published by the Free Software Foundation, either version 3 of the
  License, or (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program.  If not, see <http://www.gnu.org/licenses/>.
==============================================================================*/

class RequestResponse {
	private $body_;
	private $request_;
	private $project_;
	private $contents_;
	private $header_;
	private $isFile = false;
	protected $config;
	protected $errorCodes_ = array(
			'FOLDER_NOT_FOUND' => 0xcb,
			'INTERNAL' => 1,
			'NONE' => 0,
			'NOT_FOUND' => 7,
			'NULL'=> 2,
	);
	public function __construct($config) {
		$this->config = $config;
		$this->body_   = array();
		$this->header_ = array("_t" => "mfheader");
	}
	public function work($json) {}
	public function setRP($json) {
		if(isset($json['request'])){
			$this->request_ = $json['request'];
		}

		if(isset($json['project'])){
			$this->project_ = $json['project'];
		}
	}
	public function send() {
		if(!$this->isFile){
			$content = json_encode(array(
				"request" => $this->request_,
				"project" => $this->project_,
				"body" => $this->body_
			));	//The current server doesn't support JSON_NUMERIC_CHECK.

			//Todo: Find a more elegant way to turn "body":[] into "body":{}
			if (empty($this->body_)) {
				$content = str_replace('[]', '{}', $content) ;
			}
			echo $content;
		} else {
			header('Content-Type: application/octet-stream');
			echo $this->contents_;
		}
	}
	public function setResponseAsFile(){
		$this->isFile = true;
	}
	public function setContent($value) {
		$this->contents_ = $value;
	}
	public function addBody($key, $value) {
		$this->body_[$key] = $value;
	}
	public function addHeader($key, $value) {
		$this->header_[$key] = $value;
	}
	public function error($code) {
		$this->addBody("error", array(
			"errcode" => (string)$this->errorCodes_[$code]
		));
	}
	public function log($text) {
		if ($this->config["logging"] == "enabled") {
			$file = fopen($this->config["request_log"], "a");
			if ($file) {
				fwrite($file, "[".date("Y-m-d H:i:s")."] ".get_class($this).": ".$text."\n");
				fclose($file);
			}
		}
	}
}
